using System.Text;
using Xunit;

namespace GmIconPack.Tests;

public sealed class NpkIndexTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("gmiconpack-npk-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void Key_is_the_phrase_padded_with_dnf_and_zero_terminated()
    {
        Assert.Equal(256, NpkIndex.Key.Length);
        Assert.Equal("puchikon@neople dungeon and fighter DNFDNF", Encoding.ASCII.GetString(NpkIndex.Key, 0, 42));
        Assert.Equal((byte)'F', NpkIndex.Key[254]); // (254 - 36) % 3 == 2
        Assert.Equal(0, NpkIndex.Key[255]);
    }

    [Fact]
    public void Xor_names_are_found_by_pvf_icon_path()
    {
        File.WriteAllBytes(Path.Combine(folder, "sprite_item.NPK"),
            Synthetic.Npk(("sprite/item/weapon/a.img", [1, 2, 3]), ("sprite/Item/Common/B.img", [4, 5])));

        var index = NpkIndex.Build(folder);

        Assert.Equal((2, 1), (index.Count, index.PackCount));
        Assert.Equal(new byte[] { 1, 2, 3 }, index.ReadImg("Item/weapon/a.img"));
        Assert.Equal(new byte[] { 4, 5 }, index.ReadImg(@"item\common\b.IMG"));
        Assert.Null(index.ReadImg("Item/weapon/missing.img"));
    }

    [Fact]
    public void First_pack_in_name_order_wins_and_foreign_files_are_skipped()
    {
        File.WriteAllBytes(Path.Combine(folder, "b.npk"), Synthetic.Npk(("sprite/item/a.img", [2])));
        File.WriteAllBytes(Path.Combine(folder, "a.NPK"), Synthetic.Npk(("sprite/item/a.img", [1])));
        File.WriteAllBytes(Path.Combine(folder, "c.npk"), new byte[32]);

        var index = NpkIndex.Build(folder);

        Assert.Equal(2, index.PackCount);
        Assert.Equal(new byte[] { 1 }, index.ReadImg("Item/a.img"));
    }

    [Theory]
    [InlineData("Item/weapon/x.img", "sprite/item/weapon/x.img")]
    [InlineData(@"\Item\weapon\x.img", "sprite/item/weapon/x.img")]
    [InlineData("Sprite/Item/x.img", "sprite/item/x.img")]
    public void Icon_paths_normalize_under_sprite(string input, string expected) =>
        Assert.Equal(expected, NpkIndex.NormalizeImgPath(input));
}
