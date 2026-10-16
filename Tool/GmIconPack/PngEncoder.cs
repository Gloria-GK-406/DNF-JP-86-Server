using System.Buffers.Binary;
using System.IO.Compression;

namespace GmIconPack;

/// <summary>Minimal PNG writer: 8-bit RGBA (color type 6), one IDAT, filter 0 on every row.</summary>
internal static class PngEncoder
{
    internal static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var stride = width * 4;
        if (rgba.Length != stride * height) throw new ArgumentException($"expected {stride * height} RGBA bytes, got {rgba.Length}", nameof(rgba));

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // RGBA
        header[10] = header[11] = header[12] = 0; // deflate, adaptive filtering, no interlace
        WriteChunk(output, "IHDR"u8, header);

        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgba.Slice(y * stride, stride));
            }
        }
        WriteChunk(output, "IDAT"u8, idat.GetBuffer().AsSpan(0, (int)idat.Length));
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, body.Length);
        output.Write(word);
        output.Write(type);
        output.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32.Append(Crc32.Compute(type), body));
        output.Write(word);
    }
}
