using System.IO.Compression;
using System.Text.Json;
using Xunit;
using static GmIconPack.Tests.Synthetic;

namespace GmIconPack.Tests;

public sealed class IconPackBuilderTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("gmiconpack-pack-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void Pack_holds_one_png_per_resolved_item_and_a_camel_case_manifest()
    {
        var icon = new byte[28 * 28 * 4];
        for (var i = 0; i < icon.Length; i++) icon[i] = (byte)i;
        var img = Img(new Picture(0x10, 0x06, 28, 28, icon), new LinkTo(0));
        File.WriteAllBytes(Path.Combine(folder, "sprite_item.NPK"), Npk(("sprite/item/a.img", img), ("sprite/item/bad.img", new byte[40])));
        var index = NpkIndex.Build(folder);
        IconRequest[] requests =
        [
            new(10, "Item/a.img", 0), new(11, "item/A.img", 1), new(12, "Item/a.img", 9),
            new(13, "Item/missing.img", 0), new(14, null, 0, IconFailure.NoIconSection), new(15, "Item/bad.img", 0),
        ];

        using var zipBytes = new MemoryStream();
        var summary = IconPackBuilder.Build(requests, index.ReadImg, zipBytes, "ab12", new DateTimeOffset(2026, 10, 16, 9, 2, 3, TimeSpan.FromHours(8)));

        Assert.Equal((6, 2, 1), (summary.Candidates, summary.Resolved, summary.UniqueFrames));
        Assert.Equal(new Dictionary<IconFailure, int>
        {
            [IconFailure.NoIconSection] = 1, [IconFailure.ImgNotFound] = 1, [IconFailure.NotImg] = 1, [IconFailure.FrameOutOfRange] = 1,
        }, summary.Missing);

        zipBytes.Position = 0;
        using var zip = new ZipArchive(zipBytes, ZipArchiveMode.Read);
        Assert.Equal(["icons/10.png", "icons/11.png", "manifest.json"], zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        var png = ReadEntry(zip, "icons/11.png");
        var (w, h, rgba) = ReadPng(png);
        Assert.Equal((28, 28), (w, h));
        Assert.Equal(icon[2], rgba[0]); // B,G,R,A -> R,G,B,A
        Assert.Equal(png, ReadEntry(zip, "icons/10.png"));

        using var manifest = JsonDocument.Parse(ReadEntry(zip, "manifest.json"));
        Assert.Equal("2026-10-16T01:02:03Z", manifest.RootElement.GetProperty("generatedAt").GetString());
        Assert.Equal("ab12", manifest.RootElement.GetProperty("pvfSha256").GetString());
        Assert.Equal(2, manifest.RootElement.GetProperty("count").GetInt32());
    }

    [Theory]
    [InlineData("[hat avatar]", PvfItemCatalog.EquipmentClass.Avatar)]
    [InlineData("`[weapon avatar]` ", PvfItemCatalog.EquipmentClass.Avatar)]
    [InlineData("[creature]", PvfItemCatalog.EquipmentClass.Creature)]
    [InlineData("[artifact red]", PvfItemCatalog.EquipmentClass.Equipment)]
    [InlineData("[weapon]", PvfItemCatalog.EquipmentClass.Equipment)]
    [InlineData(null, PvfItemCatalog.EquipmentClass.Equipment)]
    internal void Equipment_type_classification_mirrors_the_server(string? type, PvfItemCatalog.EquipmentClass expected) =>
        Assert.Equal(expected, PvfItemCatalog.Classify(type));

    private static byte[] ReadEntry(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
