using System.Buffers.Binary;
using System.IO.Compression;

namespace GmIconPack;

/// <summary>Why an icon could not be produced. Also the summary's breakdown keys.</summary>
internal enum IconFailure
{
    NoScript,
    NoIconSection,
    ImgNotFound,
    NotImg,
    UnsupportedVersion,
    UnsupportedFormat,
    FrameOutOfRange,
    Malformed,
}

internal sealed class ImgException(IconFailure reason, string message) : Exception(message)
{
    public IconFailure Reason { get; } = reason;
}

/// <summary>One decoded frame, top-down RGBA8888.</summary>
internal sealed record ImgFrame(int Width, int Height, byte[] Rgba);

/// <summary>
/// "Neople Img File" (v2, v4) reader. Header: magic[16], i32 indexSize, i32 reserved, i32 version, i32 frameCount;
/// v4 then carries i32 paletteCount + paletteCount x 4 bytes. The frame index follows: a link is
/// (i32 0x11, i32 target) = 8 bytes, a picture is 9 x i32 (format, compression, w, h, size, keyX, keyY, maxW, maxH) = 36 bytes.
/// Picture data follows the index in frame order; links own no data.
/// </summary>
internal sealed class ImgFile
{
    internal static readonly byte[] Magic = "Neople Img File\0"u8.ToArray();
    internal const int HeaderSize = 32;
    internal const int Argb1555 = 0x0E, Argb4444 = 0x0F, Argb8888 = 0x10, Link = 0x11;
    internal const int Uncompressed = 0x05, Zlib = 0x06;

    private readonly byte[] blob;
    private readonly Frame[] frames;
    private readonly byte[]? palette; // v4 only, raw 4-byte entries

    private readonly record struct Frame(int Format, int Compression, int Width, int Height, int Size, int DataOffset, int LinkTarget);

    public int Version { get; }
    public int FrameCount => frames.Length;

    private ImgFile(byte[] blob, int version, Frame[] frames, byte[]? palette)
    {
        this.blob = blob;
        Version = version;
        this.frames = frames;
        this.palette = palette;
    }

    public static ImgFile Parse(byte[] blob)
    {
        if (blob.Length < HeaderSize || !blob.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new ImgException(IconFailure.NotImg, "missing IMG magic");
        var span = blob.AsSpan();
        var indexSize = I32(span, 16);
        var version = I32(span, 24);
        var count = I32(span, 28);
        if (version is not (2 or 4)) throw new ImgException(IconFailure.UnsupportedVersion, $"IMG v{version}");
        if (count < 0 || indexSize < 0) throw new ImgException(IconFailure.Malformed, "negative frame count or index size");

        var pos = HeaderSize;
        byte[]? palette = null;
        if (version == 4)
        {
            Need(blob, pos, 4);
            var paletteCount = I32(span, pos);
            pos += 4;
            if (paletteCount < 0) throw new ImgException(IconFailure.Malformed, "negative palette count");
            Need(blob, pos, (long)paletteCount * 4);
            palette = blob.AsSpan(pos, paletteCount * 4).ToArray();
            pos += paletteCount * 4;
        }

        var indexStart = pos;
        var parsed = new Frame[count];
        for (var i = 0; i < count; i++)
        {
            Need(blob, pos, 8);
            var format = I32(span, pos);
            if (format == Link)
            {
                parsed[i] = new(Link, 0, 0, 0, 0, -1, I32(span, pos + 4));
                pos += 8;
                continue;
            }
            Need(blob, pos, 36);
            parsed[i] = new(format, I32(span, pos + 4), I32(span, pos + 8), I32(span, pos + 12), I32(span, pos + 16), -1, -1);
            pos += 36;
        }
        // The header's own index size is the cross-check that the per-frame layout above is right.
        if (pos - indexStart != indexSize)
            throw new ImgException(IconFailure.Malformed, $"frame index spans {pos - indexStart} bytes, header says {indexSize}");

        for (var i = 0; i < count; i++)
        {
            if (parsed[i].Format == Link) continue;
            if (parsed[i].Size < 0) throw new ImgException(IconFailure.Malformed, $"frame {i} has negative size");
            parsed[i] = parsed[i] with { DataOffset = pos };
            pos += parsed[i].Size;
        }
        return new(blob, version, parsed, palette);
    }

    /// <summary>Follows links to the picture frame that owns the pixels.</summary>
    public int ResolveLink(int index)
    {
        if (index < 0 || index >= frames.Length)
            throw new ImgException(IconFailure.FrameOutOfRange, $"frame {index} of {frames.Length}");
        var at = index;
        for (var hops = 0; frames[at].Format == Link; hops++)
        {
            if (hops >= frames.Length) throw new ImgException(IconFailure.Malformed, $"link cycle from frame {index}");
            var target = frames[at].LinkTarget;
            if (target < 0 || target >= frames.Length)
                throw new ImgException(IconFailure.FrameOutOfRange, $"link to frame {target} of {frames.Length}");
            at = target;
        }
        return at;
    }

    public ImgFrame Decode(int index)
    {
        var frame = frames[ResolveLink(index)];

        if (frame.Width <= 0 || frame.Height <= 0) throw new ImgException(IconFailure.Malformed, $"frame is {frame.Width}x{frame.Height}");
        if (frame.Format is not (Argb1555 or Argb4444 or Argb8888)) throw new ImgException(IconFailure.UnsupportedFormat, $"color format 0x{frame.Format:X2}");
        if (frame.Compression is not (Uncompressed or Zlib)) throw new ImgException(IconFailure.UnsupportedFormat, $"compression 0x{frame.Compression:X2}");
        if ((long)frame.DataOffset + frame.Size > blob.Length) throw new ImgException(IconFailure.Malformed, "frame data runs past the IMG end");

        ReadOnlySpan<byte> data = blob.AsSpan(frame.DataOffset, frame.Size);
        if (frame.Compression == Zlib)
        {
            try { data = Inflate(data); }
            catch (InvalidDataException e) { throw new ImgException(IconFailure.Malformed, "zlib: " + e.Message); }
        }

        var pixels = frame.Width * frame.Height;
        // v4 stores palette indices under the 1555 tag, one byte per pixel.
        if (palette is not null && frame.Format == Argb1555 && data.Length == pixels)
            return new(frame.Width, frame.Height, FromPalette(data, palette));
        var bpp = frame.Format == Argb8888 ? 4 : 2;
        if (data.Length < (long)pixels * bpp)
            throw new ImgException(IconFailure.Malformed, $"frame data {data.Length} bytes, need {pixels * bpp}");
        return new(frame.Width, frame.Height, ToRgba(frame.Format, data, pixels));
    }

    /// <summary>
    /// 8888 is stored B,G,R,A. 4444 nibbles widen by x17. 1555 channels widen by c*255/31 (integer), alpha bit -> 0/255.
    /// </summary>
    internal static byte[] ToRgba(int format, ReadOnlySpan<byte> data, int pixels)
    {
        var rgba = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            var o = i * 4;
            if (format == Argb8888)
            {
                rgba[o] = data[o + 2]; rgba[o + 1] = data[o + 1]; rgba[o + 2] = data[o]; rgba[o + 3] = data[o + 3];
                continue;
            }
            var v = BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 2)..]);
            if (format == Argb4444)
            {
                rgba[o] = (byte)(((v >> 8) & 15) * 17); rgba[o + 1] = (byte)(((v >> 4) & 15) * 17);
                rgba[o + 2] = (byte)((v & 15) * 17); rgba[o + 3] = (byte)(((v >> 12) & 15) * 17);
            }
            else
            {
                rgba[o] = (byte)(((v >> 10) & 31) * 255 / 31); rgba[o + 1] = (byte)(((v >> 5) & 31) * 255 / 31);
                rgba[o + 2] = (byte)((v & 31) * 255 / 31); rgba[o + 3] = (v & 0x8000) != 0 ? (byte)255 : (byte)0;
            }
        }
        return rgba;
    }

    // Palette entries share the 8888 byte order (B,G,R,A). Unverified: no v4 IMG has been seen under sprite/item.
    private static byte[] FromPalette(ReadOnlySpan<byte> indices, byte[] palette)
    {
        var rgba = new byte[indices.Length * 4];
        for (var i = 0; i < indices.Length; i++)
        {
            var p = indices[i] * 4;
            if (p + 4 > palette.Length) throw new ImgException(IconFailure.Malformed, $"palette index {indices[i]} of {palette.Length / 4}");
            rgba[i * 4] = palette[p + 2]; rgba[i * 4 + 1] = palette[p + 1]; rgba[i * 4 + 2] = palette[p]; rgba[i * 4 + 3] = palette[p + 3];
        }
        return rgba;
    }

    private static byte[] Inflate(ReadOnlySpan<byte> data)
    {
        using var input = new MemoryStream(data.ToArray(), writable: false);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    private static int I32(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);

    private static void Need(byte[] blob, int pos, long length)
    {
        if (pos + length > blob.Length) throw new ImgException(IconFailure.Malformed, "truncated IMG header");
    }
}
