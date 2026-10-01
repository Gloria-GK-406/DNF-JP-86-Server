using System.Collections.Frozen;
using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEquipmentDefinitionSourceTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(65535, 65535)]
    public async Task Missing_zero_normal_and_maximum_durability_preserve_complete_equipment_facts(
        int? durability, int? expected)
    {
        using var archive = new BuiltArchive(BuildEquipment(durability: durability));
        var source = new PvfEquipmentDefinitionSource(archive.Provider);
        Assert.Equal(1, await source.InitializeAsync(default));
        var result = await source.GetEquipmentAsync(9001, default);
        Assert.NotNull(result);
        Assert.Equal((80, 82, 2, "weapon", expected is int maximum ? (ushort?)maximum : null),
            (result.MinimumLevel, result.Grade, result.Rarity, result.EquipmentType, result.MaxDurability));
        Assert.IsAssignableFrom<FrozenSet<string>>(result.ImpossibleContents);
        Assert.Contains("upgrade", result.ImpossibleContents);
        Assert.Contains("separate upgrade", result.ImpossibleContents);
    }

    [Fact]
    public async Task Valid_unknown_type_is_preserved_and_absent_item_is_normal_not_found()
    {
        using var archive = new BuiltArchive(BuildEquipment(equipmentType: "[future equipment]"));
        var source = new PvfEquipmentDefinitionSource(archive.Provider);
        Assert.Equal("future equipment", (await source.GetEquipmentAsync(9001, default))!.EquipmentType);
        Assert.Null(await source.GetEquipmentAsync(9002, default));
        Assert.Null(await source.GetEquipmentAsync(9002, default));
    }

    [Theory]
    [InlineData("[minimum level]", "missing")]
    [InlineData("[minimum level]", "duplicate")]
    [InlineData("[minimum level]", "float")]
    [InlineData("[minimum level]", "negative")]
    [InlineData("[grade]", "missing")]
    [InlineData("[grade]", "duplicate")]
    [InlineData("[grade]", "float")]
    [InlineData("[grade]", "negative")]
    [InlineData("[rarity]", "missing")]
    [InlineData("[rarity]", "duplicate")]
    [InlineData("[rarity]", "float")]
    [InlineData("[rarity]", "negative")]
    [InlineData("[rarity]", "too-large")]
    [InlineData("[durability]", "duplicate")]
    [InlineData("[durability]", "float")]
    [InlineData("[durability]", "negative")]
    [InlineData("[durability]", "too-large")]
    [InlineData("[durability]", "extra-value")]
    [InlineData("[equipment type]", "missing")]
    [InlineData("[equipment type]", "duplicate")]
    [InlineData("[equipment type]", "number")]
    [InlineData("[equipment type]", "empty")]
    [InlineData("[equipment type]", "negative-subtype")]
    [InlineData("[equipment type]", "float-subtype")]
    [InlineData("[equipment type]", "extra-value")]
    [InlineData("[impossible contents]", "number")]
    [InlineData("[impossible contents]", "empty")]
    [InlineData("[impossible contents]", "duplicate")]
    [InlineData("[impossible contents]", "duplicate-value")]
    public async Task Illegal_fields_throw_with_item_path_and_field_on_every_read(string field, string defect)
    {
        using var archive = new BuiltArchive(BuildEquipment(field, defect));
        var source = new PvfEquipmentDefinitionSource(archive.Provider);
        // Preparing the index does not eagerly parse all equipment templates.
        Assert.Equal(1, await source.InitializeAsync(default));
        Assert.Null(await source.GetEquipmentAsync(9002, default));
        for (var read = 0; read < 2; read++)
        {
            var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await source.GetEquipmentAsync(9001, default));
            Assert.Contains("9001", exception.Message);
            Assert.Contains("equipment/test.equ", exception.Message);
            Assert.Contains(field, exception.Message);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("duplicate-id")]
    [InlineData("negative-id")]
    [InlineData("zero-id")]
    [InlineData("dangling-id")]
    [InlineData("two-ids")]
    [InlineData("path-without-id")]
    [InlineData("empty-path")]
    [InlineData("root-path")]
    [InlineData("parent-path")]
    [InlineData("wrong-file-type")]
    [InlineData("truncated-token")]
    public async Task Required_index_errors_fail_preparation_and_are_not_cached_as_empty(string defect)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "test.equ").Tag("[unused]").Int(0).End();
        if (defect == "wrong-file-type")
            builder.AddText("equipment", "equipment.lst", "not a script");
        else if (defect == "truncated-token")
            builder.AddRaw("equipment", "equipment.lst", [0], dataType: 1);
        else if (defect != "missing")
        {
            var index = builder.AddScript("equipment", "equipment.lst");
            switch (defect)
            {
                case "duplicate-id": index.Int(9001).Quoted("test.equ").Int(9001).Quoted("test.equ"); break;
                case "negative-id": index.Int(-1).Quoted("test.equ"); break;
                case "zero-id": index.Int(0).Quoted("test.equ"); break;
                case "dangling-id": index.Int(9001); break;
                case "two-ids": index.Int(9001).Int(9002).Quoted("test.equ"); break;
                case "path-without-id": index.Quoted("test.equ"); break;
                case "empty-path": index.Int(9001).Quoted(""); break;
                case "root-path": index.Int(9001).Quoted("/test.equ"); break;
                case "parent-path": index.Int(9001).Quoted("../test.equ"); break;
            }
            index.End();
        }
        using var archive = new BuiltArchive(builder);
        var source = new PvfEquipmentDefinitionSource(archive.Provider);
        for (var read = 0; read < 2; read++)
        {
            var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await source.InitializeAsync(default));
            Assert.Contains("Equipment", exception.Message);
            Assert.Contains("equipment/equipment.lst", exception.Message);
            Assert.Contains("index", exception.Message);
        }
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await source.GetEquipmentAsync(9001, default));
    }

    [Fact]
    public async Task Indexed_missing_template_is_content_error_instead_of_normal_not_found()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(9001).Quoted("absent.equ").End();
        using var archive = new BuiltArchive(builder);
        var source = new PvfEquipmentDefinitionSource(archive.Provider);
        Assert.Equal(1, await source.InitializeAsync(default));
        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await source.GetEquipmentAsync(9001, default));
        Assert.Contains("9001", exception.Message);
        Assert.Contains("equipment/absent.equ", exception.Message);
        Assert.Contains("template", exception.Message);
    }

    [SkippableFact]
    public async Task A21_all_three_jewelry_types_without_durability_and_weapon_and_armor_return_complete_facts()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentDefinitionSource(provider);
        Assert.True(await source.InitializeAsync(default) > 0);
        var bracelet = await source.GetEquipmentAsync(100310498, default);
        Assert.NotNull(bracelet);
        Assert.Equal((80, 2, "wrist"), (bracelet.MinimumLevel, bracelet.Rarity, bracelet.EquipmentType));
        Assert.Null(bracelet.MaxDurability);
        foreach (var (itemId, type) in new[] { (100310755, "amulet"), (100320520, "ring") })
        {
            var jewelry = await source.GetEquipmentAsync(itemId, default);
            Assert.NotNull(jewelry);
            Assert.Equal((80, 82, 2, type),
                (jewelry.MinimumLevel, jewelry.Grade, jewelry.Rarity, jewelry.EquipmentType));
            Assert.Null(jewelry.MaxDurability);
        }
        var weapon = await source.GetEquipmentAsync(101010437, default);
        Assert.NotNull(weapon);
        Assert.Equal("weapon", weapon.EquipmentType);
        Assert.Equal((ushort)45, weapon.MaxDurability);
        var armor = await source.GetEquipmentAsync(100170123, default);
        Assert.NotNull(armor);
        Assert.Equal("shoulder", armor.EquipmentType);
        Assert.NotNull(armor.MaxDurability);
    }

    private static PvfArchiveBuilder BuildEquipment(string? defectField = null, string? defect = null,
        int? durability = 45, string equipmentType = "[weapon]")
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(9001).Quoted("test.equ").End();
        var script = builder.AddScript("equipment", "test.equ");
        foreach (var (field, value) in new[]
        {
            ("[minimum level]", 80), ("[grade]", 82), ("[rarity]", 2), ("[durability]", durability ?? 0),
        })
        {
            if (field == "[durability]" && durability is null || field == defectField && defect == "missing")
                continue;
            script.Tag(field);
            if (field == defectField && defect == "float") script.Single(value);
            else script.Int(field == defectField ? defect switch
            {
                "negative" => -1,
                "too-large" => field == "[rarity]" ? 7 : 65536,
                _ => value,
            } : value);
            if (field == defectField && defect == "duplicate") script.Tag(field).Int(value);
            if (field == defectField && defect == "extra-value") script.Int(value);
        }
        if (defectField != "[equipment type]" || defect != "missing")
        {
            script.Tag("[equipment type]");
            if (defectField == "[equipment type]" && defect == "number") script.Int(17);
            else script.Quoted(defectField == "[equipment type]" && defect == "empty" ? "" : equipmentType);
            if (defectField == "[equipment type]" && defect == "float-subtype") script.Single(17);
            else script.Int(defectField == "[equipment type]" && defect == "negative-subtype" ? -1 : 17);
            if (defectField == "[equipment type]" && defect == "duplicate")
                script.Tag("[equipment type]").Quoted(equipmentType);
            if (defectField == "[equipment type]" && defect == "extra-value") script.Int(17);
        }
        script.Tag("[impossible contents]");
        if (defectField == "[impossible contents]" && defect == "number") script.Int(1);
        else script.Quoted(defectField == "[impossible contents]" && defect == "empty" ? "" : "[upgrade]")
            .Quoted("[separate upgrade]");
        if (defectField == "[impossible contents]" && defect == "duplicate-value") script.Quoted("upgrade");
        if (defectField == "[impossible contents]" && defect == "duplicate")
            script.Tag("[impossible contents]").Quoted("upgrade");
        script.Tag("[/impossible contents]").End();
        return builder;
    }

    private sealed class BuiltArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"dfo-equipment-facts-{Guid.NewGuid():N}.pvf");
        public PvfArchiveProvider Provider { get; }

        public BuiltArchive(PvfArchiveBuilder builder)
        {
            File.WriteAllBytes(path, builder.Build());
            Provider = new(new PvfContentOptions { ArchivePath = path });
        }

        public void Dispose()
        {
            Provider.Dispose();
            File.Delete(path);
        }
    }
}
