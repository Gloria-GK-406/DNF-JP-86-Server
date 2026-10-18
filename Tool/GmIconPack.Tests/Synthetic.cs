using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace GmIconPack.Tests;

/// <summary>Builders for synthetic NPK/IMG buffers and an independent PNG reader for round-trip checks.</summary>
internal static class Synthetic
{
    internal abstract record ImgFrameSpec;
    internal sealed record Picture(int Format, int Compression, int Width, int Height, byte[] Pixels) : ImgFrameSpec;
    internal sealed record LinkTo(int Target) : ImgFrameSpec;

    public static byte[] Img(params ImgFrameSpec[] frames) => Img(2, null, null, frames);

    public static byte[] Img(int version, byte[]? palette, int? indexSizeOverride, params ImgFrameSpec[] frames)
    {
        var index = new MemoryStream();
        var data = new MemoryStream();
        foreach (var frame in frames)
        {
            switch (frame)
            {
                case LinkTo link:
                    I32(index, 0x11); I32(index, link.Target);
                    break;
                case Picture p:
                    var stored = p.Compression == 0x06 ? Deflate(p.Pixels) : p.Pixels;
                    foreach (var v in new[] { p.Format, p.Compression, p.Width, p.Height, stored.Length, 0, 0, p.Width, p.Height }) I32(index, v);
                    data.Write(stored);
                    break;
            }
        }
        var output = new MemoryStream();
        output.Write("Neople Img File\0"u8);
        I32(output, indexSizeOverride ?? (int)index.Length);
        I32(output, 0);
        I32(output, version);
        I32(output, frames.Length);
        if (version == 4)
        {
            I32(output, palette!.Length / 4);
            output.Write(palette);
        }
        index.WriteTo(output);
        data.WriteTo(output);
        return output.ToArray();
    }

    public static byte[] Npk(params (string Name, byte[] Blob)[] entries)
    {
        var output = new MemoryStream();
        output.Write("NeoplePack_Bill\0"u8);
        I32(output, entries.Length);
        var offset = 16 + 4 + entries.Length * (8 + 256);
        foreach (var (name, blob) in entries)
        {
            I32(output, offset); I32(output, blob.Length);
            var raw = new byte[256];
            Encoding.ASCII.GetBytes(name).CopyTo(raw, 0);
            for (var i = 0; i < raw.Length; i++) raw[i] ^= NpkIndex.Key[i];
            output.Write(raw);
            offset += blob.Length;
        }
        foreach (var (_, blob) in entries) output.Write(blob);
        return output.ToArray();
    }

    public static byte[] Deflate(byte[] raw)
    {
        var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
        return output.ToArray();
    }

    public static byte[] U16s(params ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), values[i]);
        return bytes;
    }

    /// <summary>Parses a PNG written by <see cref="PngEncoder"/>: checks every CRC, IHDR, filter bytes; returns RGBA.</summary>
    public static (int Width, int Height, byte[] Rgba) ReadPng(byte[] png)
    {
        Assert.True(png.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
        var pos = 8;
        var types = new List<string>();
        int width = 0, height = 0;
        var idat = new MemoryStream();
        while (pos < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var type = Encoding.ASCII.GetString(png, pos + 4, 4);
            var body = png.AsSpan(pos + 8, length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + length));
            Assert.Equal(Crc32.Compute(png.AsSpan(pos + 4, 4 + length)), crc);
            types.Add(type);
            if (type == "IHDR")
            {
                Assert.Equal(13, length);
                width = BinaryPrimitives.ReadInt32BigEndian(body);
                height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, body[8..].ToArray());
            }
            if (type == "IDAT") idat.Write(body);
            pos += 12 + length;
        }
        Assert.Equal(["IHDR", "IDAT", "IEND"], types);

        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var raw = new MemoryStream();
        z.CopyTo(raw);
        var scan = raw.ToArray();
        var stride = width * 4;
        Assert.Equal((stride + 1) * height, scan.Length);
        var rgba = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            Assert.Equal(0, scan[y * (stride + 1)]);
            Array.Copy(scan, y * (stride + 1) + 1, rgba, y * stride, stride);
        }
        return (width, height, rgba);
    }

    private static void I32(Stream stream, int value)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(word, value);
        stream.Write(word);
    }
}
