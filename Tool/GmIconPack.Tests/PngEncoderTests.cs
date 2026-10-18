using System.Text;
using Xunit;

namespace GmIconPack.Tests;

public sealed class PngEncoderTests
{
    [Fact]
    public void Crc32_matches_the_standard_check_value() =>
        Assert.Equal(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("123456789")));

    [Fact]
    public void Crc32_append_equals_one_pass() =>
        Assert.Equal(Crc32.Compute("IDATbody"u8), Crc32.Append(Crc32.Compute("IDAT"u8), "body"u8));

    [Fact]
    public void Encoded_rgba_reads_back_identically()
    {
        const int w = 3, h = 2;
        var rgba = new byte[w * h * 4];
        for (var i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i * 37 + 11);

        var (width, height, decoded) = Synthetic.ReadPng(PngEncoder.Encode(w, h, rgba));

        Assert.Equal((w, h), (width, height));
        Assert.Equal(rgba, decoded);
    }

    [Fact]
    public void Wrong_pixel_count_is_rejected() =>
        Assert.Throws<ArgumentException>(() => PngEncoder.Encode(2, 2, new byte[15]));
}
