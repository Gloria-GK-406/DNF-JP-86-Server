using Xunit;
using static GmIconPack.Tests.Synthetic;

namespace GmIconPack.Tests;

public sealed class ImgFileTests
{
    // 2x2 ARGB8888 stored B,G,R,A.
    private static readonly Picture Bgra = new(0x10, 0x05, 2, 2, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 255]);
    // 2x1 ARGB4444, zlib: 0xF84C -> a15 r8 g4 b12; 0x1234 -> a1 r2 g3 b4.
    private static readonly Picture Nibbles = new(0x0F, 0x06, 2, 1, U16s(0xF84C, 0x1234));
    // 1x2 ARGB1555: alpha bit set, r31 g16 b1; then alpha clear, all 31.
    private static readonly Picture Packed = new(0x0E, 0x05, 1, 2, U16s((ushort)(0x8000 | (31 << 10) | (16 << 5) | 1), 0x7FFF));

    private static ImgFile Sample() => ImgFile.Parse(Img(Bgra, Nibbles, Packed, new LinkTo(0)));

    [Fact]
    public void Argb8888_swaps_bgra_to_rgba()
    {
        var frame = Sample().Decode(0);
        Assert.Equal((2, 2), (frame.Width, frame.Height));
        Assert.Equal(new byte[] { 3, 2, 1, 4, 7, 6, 5, 8, 11, 10, 9, 12, 15, 14, 13, 255 }, frame.Rgba);
    }

    [Fact]
    public void Argb4444_zlib_nibbles_widen_by_17()
    {
        var frame = Sample().Decode(1);
        Assert.Equal((2, 1), (frame.Width, frame.Height));
        Assert.Equal(new byte[] { 136, 68, 204, 255, 34, 51, 68, 17 }, frame.Rgba);
    }

    [Fact]
    public void Argb1555_channels_widen_by_255_over_31_and_alpha_is_one_bit()
    {
        var frame = Sample().Decode(2);
        // 16*255/31 = 131 and 1*255/31 = 8 under integer division.
        Assert.Equal(new byte[] { 255, 131, 8, 255, 255, 255, 255, 0 }, frame.Rgba);
    }

    [Fact]
    public void Link_frame_decodes_its_target()
    {
        var img = Sample();
        Assert.Equal(4, img.FrameCount);
        Assert.Equal(img.Decode(0).Rgba, img.Decode(3).Rgba);
    }

    [Fact]
    public void Data_offsets_skip_links()
    {
        // A link between two pictures owns no data, so the second picture still starts right after the first.
        var img = ImgFile.Parse(Img(Packed, new LinkTo(0), Bgra));
        Assert.Equal(Sample().Decode(0).Rgba, img.Decode(2).Rgba);
    }

    [Fact]
    public void V4_palette_indices_use_bgra_entries()
    {
        byte[] palette = [10, 20, 30, 40, 50, 60, 70, 80];
        var img = ImgFile.Parse(Img(4, palette, null, new Picture(0x0E, 0x06, 2, 1, [1, 0])));
        Assert.Equal(4, img.Version);
        Assert.Equal(new byte[] { 70, 60, 50, 80, 30, 20, 10, 40 }, img.Decode(0).Rgba);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(-1)]
    public void Frame_outside_the_index_is_out_of_range(int index) =>
        Assert.Equal(IconFailure.FrameOutOfRange, Assert.Throws<ImgException>(() => Sample().Decode(index)).Reason);

    [Fact]
    public void Dangling_link_is_out_of_range() =>
        Assert.Equal(IconFailure.FrameOutOfRange,
            Assert.Throws<ImgException>(() => ImgFile.Parse(Img(Bgra, new LinkTo(7))).Decode(1)).Reason);

    [Fact]
    public void Link_cycle_is_malformed() =>
        Assert.Equal(IconFailure.Malformed,
            Assert.Throws<ImgException>(() => ImgFile.Parse(Img(new LinkTo(1), new LinkTo(0))).Decode(0)).Reason);

    [Fact]
    public void Unknown_color_format_is_unsupported() =>
        Assert.Equal(IconFailure.UnsupportedFormat,
            Assert.Throws<ImgException>(() => ImgFile.Parse(Img(new Picture(0x12, 0x05, 1, 1, [0, 0, 0, 0]))).Decode(0)).Reason);

    [Fact]
    public void Other_versions_are_unsupported() =>
        Assert.Equal(IconFailure.UnsupportedVersion, Assert.Throws<ImgException>(() => ImgFile.Parse(Img(5, null, null, Bgra))).Reason);

    [Fact]
    public void Missing_magic_is_not_an_img() =>
        Assert.Equal(IconFailure.NotImg, Assert.Throws<ImgException>(() => ImgFile.Parse(new byte[64])).Reason);

    [Fact]
    public void Index_size_disagreeing_with_the_frames_is_malformed() =>
        Assert.Equal(IconFailure.Malformed, Assert.Throws<ImgException>(() => ImgFile.Parse(Img(2, null, 40, Bgra))).Reason);

    [Fact]
    public void Truncated_pixel_data_is_malformed()
    {
        var blob = Img(Bgra);
        Assert.Equal(IconFailure.Malformed, Assert.Throws<ImgException>(() => ImgFile.Parse(blob[..^4]).Decode(0)).Reason);
    }
}
