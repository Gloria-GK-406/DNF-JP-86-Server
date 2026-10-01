using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEquipmentDisjointDefinitionSourceTests
{
    [Theory]
    [InlineData(0, 200)]
    [InlineData(1, 30)]
    public async Task Destruction_reads_raw_value_grade_and_random_option_sale_rate_without_changing_npc_admission(
        int randomOption, int rate)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(9001).Quoted("test.equ").End();
        builder.AddScript("equipment", "pricetable.tbl").Tag("[sale rates]").Int(200).Int(150).Int(30).End();
        builder.AddScript("equipment", "test.equ").Tag("[value]").Int(20520)
            .Tag("[grade]").Int(57).Tag("[minimum level]").Int(55).Tag("[rarity]").Int(0)
            .Tag("[random option]").Int(randomOption).Tag("[attach type]").Quoted("[trade delete]").End();
        var path = Path.Combine(Path.GetTempPath(), $"dfo-destruction-{Guid.NewGuid():N}.pvf");
        try
        {
            File.WriteAllBytes(path, builder.Build());
            using var provider = new PvfArchiveProvider(new PvfContentOptions { ArchivePath = path });
            var source = new PvfEquipmentDisjointDefinitionSource(provider);
            Assert.Equal(new EquipmentDestructionDefinition(20520, 57, 0, rate),
                await source.GetDestructionEquipmentAsync(9001, default));
            var npc = await source.GetEquipmentAsync(9001, default);
            Assert.NotNull(npc);
            Assert.Equal(4104, npc.SellGold);
            Assert.False(npc.CanDisjoint);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public async Task Invalid_destruction_facts_fail_closed(bool omitGrade, int randomOption)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(9001).Quoted("test.equ").End();
        builder.AddScript("equipment", "pricetable.tbl").Tag("[sale rates]").Int(200).Int(150).Int(30).End();
        var item = builder.AddScript("equipment", "test.equ").Tag("[value]").Int(1000).Tag("[rarity]").Int(0)
            .Tag("[random option]").Int(randomOption);
        if (!omitGrade) item.Tag("[grade]").Int(50);
        item.End();
        var path = Path.Combine(Path.GetTempPath(), $"dfo-invalid-destruction-{Guid.NewGuid():N}.pvf");
        try
        {
            File.WriteAllBytes(path, builder.Build());
            using var provider = new PvfArchiveProvider(new PvfContentOptions { ArchivePath = path });
            Assert.Null(await new PvfEquipmentDisjointDefinitionSource(provider).GetDestructionEquipmentAsync(9001, default));
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public async Task Actual_long_sword_with_nested_skill_values_reads_its_root_destruction_facts()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentDisjointDefinitionSource(provider);
        Assert.Equal(new EquipmentDestructionDefinition(104960, 82, 2, 200),
            await source.GetDestructionEquipmentAsync(101040127, default));
    }

    [Fact]
    public async Task Skill_container_values_and_nested_grade_rarity_random_option_do_not_replace_root_facts()
    {
        await WithSkillArchive(null, async source =>
        {
            Assert.Equal(new EquipmentDestructionDefinition(104960, 82, 2, 200),
                await source.GetDestructionEquipmentAsync(9001, default));
            // This addition is scoped to destruction: preserve the existing NPC read behavior.
            Assert.Null(await source.GetEquipmentAsync(9001, default));
        });
    }

    [Theory]
    [InlineData("value")]
    [InlineData("grade")]
    [InlineData("rarity")]
    [InlineData("random option")]
    [InlineData("unclosed container")]
    [InlineData("wrong closing")]
    [InlineData("missing root value")]
    public async Task Duplicate_root_fields_and_malformed_skill_scopes_are_rejected(string defect)
    {
        await WithSkillArchive(defect, async source =>
            Assert.Null(await source.GetDestructionEquipmentAsync(9001, default)));
    }

    private static async Task WithSkillArchive(string? defect,
        Func<PvfEquipmentDisjointDefinitionSource, Task> assertion)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(9001).Quoted("skill.equ").End();
        builder.AddScript("equipment", "pricetable.tbl").Tag("[sale rates]").Int(200).Int(150).Int(30).End();
        var script = builder.AddScript("equipment", "skill.equ")
            .Tag("[grade]").Int(82).Tag("[rarity]").Int(2).Tag("[minimum level]").Int(80)
            .Tag("[random option]").Int(0);
        if (defect != "missing root value") script.Tag("[value]").Int(104960);
        script.Tag("[all skill item container]");
        for (var index = 0; index < 3; index++)
        {
            script.Tag("[all skill item]").Tag("[item growtype]").Int(index)
                .Tag("[skill apply condition]").Tag("[skill group]").Quoted("[all]")
                .Tag("[lower bound level]").Int(1).Tag("[upper bound level]").Int(85)
                .Tag("[value]").Int(1).Tag("[extra condition]")
                .Tag("[grade]").Int(999).Tag("[rarity]").Int(6).Tag("[random option]").Int(1)
                .Tag("[/extra condition]")
                .Tag(index == 0 && defect == "wrong closing" ? "[/wrong condition]" : "[/skill apply condition]")
                .Tag("[skill phase]").Quoted("[basic]").Tag("[/all skill item]");
        }
        if (defect != "unclosed container") script.Tag("[/all skill item container]");
        if (defect is "value" or "grade" or "rarity" or "random option")
            script.Tag("[" + defect + "]").Int(1);
        script.End();
        var path = Path.Combine(Path.GetTempPath(), $"dfo-skill-destruction-{Guid.NewGuid():N}.pvf");
        try
        {
            File.WriteAllBytes(path, builder.Build());
            using var provider = new PvfArchiveProvider(new PvfContentOptions { ArchivePath = path });
            await assertion(new PvfEquipmentDisjointDefinitionSource(provider));
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public async Task A21_system_disjoint_table_and_equipment_facts_are_read_from_the_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentDisjointDefinitionSource(provider);

        var table = await source.GetSystemTableAsync(default);
        Assert.NotNull(table);
        Assert.Equal((3037, 150d), (table.CubeItemId, table.CubeCreationBase));
        var expectedMultipliers = new[] { 1.12, 1.35, 0.07, 1.48, 1.8, 0.7, 1.53 };
        Assert.Equal(expectedMultipliers.Length, table.CubeCreationMultipliers.Count);
        for (var index = 0; index < expectedMultipliers.Length; index++)
            Assert.Equal(expectedMultipliers[index], table.CubeCreationMultipliers[index], 5);
        Assert.Equal(new[] { 3033, 3034, 3035, 3036 }, table.AdditionalResults[1]);
        Assert.Equal(10100115, table.ExpandRules[0].ItemId);
        Assert.False(table.ExpandRules[4].Enabled);
        Assert.Equal(10099775, table.ExpandRules[5].ItemId);
        var placements = new PvfItemPlacementDefinitionSource(provider);
        var materialIds = table.AdditionalResults.SelectMany(row => row)
            .Append(table.CubeItemId)
            .Concat(table.ExpandRules.Where(rule => rule.Enabled).Select(rule => rule.ItemId))
            .Distinct();
        foreach (var itemId in materialIds)
        {
            var placement = await placements.GetAsync(itemId, default);
            Assert.NotNull(placement);
            Assert.Equal(ItemWorldClass.Interchangeable, placement.WorldClass);
        }

        var sword = await source.GetEquipmentAsync(101010437, default);
        Assert.NotNull(sword);
        Assert.True(sword.CanDisjoint);
        Assert.True(sword.SellGold > 0);
        Assert.True(sword.MinimumLevel > 0);
        Assert.InRange(sword.Rarity, 0, 6);
        var beginner = Assert.Single(await new PvfInitialEquipmentDefinitionSource(provider)
            .GetAsync(0, default), grant => grant.SlotIndex == PvfEquipmentSlots.ByName["[weapon]"]);
        Assert.NotNull(await source.GetEquipmentAsync(beginner.ItemId, default));
        Assert.Null(await source.GetEquipmentAsync(int.MaxValue, default));
        Assert.Equal(new EquipmentDestructionDefinition(20520, 57, 0, 200),
            await source.GetDestructionEquipmentAsync(12866, default));

        Assert.Equal(new PortableDisjointMachineDefinition(0, 30),
            await source.GetPortableMachineAsync(2600468, default));
        Assert.Equal(new PortableDisjointMachineDefinition(1, 50),
            await source.GetPortableMachineAsync(2600469, default));
        Assert.Equal(new PortableDisjointMachineDefinition(2, 70),
            await source.GetPortableMachineAsync(2600470, default));
        Assert.Equal(new PortableDisjointMachineDefinition(3, 90),
            await source.GetPortableMachineAsync(2600548, default));
        Assert.Null(await source.GetPortableMachineAsync(1000, default));
    }
}
