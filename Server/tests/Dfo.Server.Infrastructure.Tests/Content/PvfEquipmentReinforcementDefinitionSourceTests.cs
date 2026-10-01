using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEquipmentReinforcementDefinitionSourceTests
{
    [SkippableFact]
    public async Task A21_normal_table_equipment_and_protection_actions_are_read_from_the_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentReinforcementDefinitionSource(provider);

        var table = await source.GetNormalTableAsync(default);
        Assert.NotNull(table);
        Assert.Equal(255, table.Rows.Count);
        Assert.Equal(12, table.NoticeLevel);
        Assert.Equal((1, 0, 1, 0, 3037, 10), Row(table, 1));
        Assert.Equal((5, 10000, 2, 1, 3037, 50), Row(table, 5));
        Assert.Equal(20, table.Rows[19].TargetLevel);
        Assert.Equal(3037, table.Rows[19].MaterialItemId);
        Assert.False(table.CostWeightByCurrentLevel.ContainsKey(19));
        Assert.Equal(0.5, table.CostWeightsByRarity[0], 5);
        Assert.Equal(1.2, table.CostWeightsByEquipmentType[0], 5);
        Assert.Equal(120, table.BaseGoldByEquipmentLevel[1]);
        Assert.Equal(0.5, table.CostWeightByCurrentLevel[0], 5);
        Assert.Equal(10, table.DestroyLevelByRarity[0]);
        Assert.Equal(50, table.MaxUpgradeLevelByRarity[1]);
        Assert.False(table.DestroyLevelByRarity.ContainsKey(2));
        Assert.Equal((3243, 40, 10), (table.DestroyBonus.ItemId,
            table.DestroyBonus.MinimumCorrectedGrade, table.DestroyBonus.UpgradeLevelBase));
        Assert.Equal(1.2, table.DestroyBonus.WeightByRarity[3], 5);
        Assert.Equal(4, table.DestroyBonus.GradeCorrectionByRarity[3]);

        var sword = await source.GetEquipmentAsync(101010437, default);
        Assert.NotNull(sword);
        Assert.Equal((3, 3, 0, 0, (ushort)45, true),
            (sword.MinimumLevel, sword.Grade, sword.Rarity,
                sword.EquipmentTypeWeightIndex, sword.MaxDurability, sword.CanReinforce));
        Assert.Null(await source.GetEquipmentAsync(int.MaxValue, default));

        Assert.Equal(new EquipmentReinforcementProtectionDefinition(
                EquipmentReinforcementProtectionKind.ResetToZero, 0),
            await source.GetProtectionAsync(7804, default));
        Assert.Equal(new EquipmentReinforcementProtectionDefinition(
                EquipmentReinforcementProtectionKind.RetainLevel, 10),
            await source.GetProtectionAsync(10007840, default));
        Assert.Null(await source.GetProtectionAsync(10004511, default)); // Amplification protection.
        Assert.Null(await source.GetProtectionAsync(10007841, default)); // Super amplification protection.
        Assert.Null(await source.GetProtectionAsync(int.MaxValue, default));
    }

    [Theory]
    [InlineData("short-row")]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("missing-cost")]
    [InlineData("float-failure-threshold")]
    [InlineData("duplicate-rarity")]
    [InlineData("odd-cost-weights")]
    [InlineData("missing-disjoint")]
    public async Task Malformed_or_missing_normal_table_fails_closed(string defect)
    {
        using var archive = new BuiltArchive(BuildArchive(defect));
        var source = new PvfEquipmentReinforcementDefinitionSource(archive.Provider);
        var table = await source.GetNormalTableAsync(default);
        Assert.Null(table);
    }

    [Fact]
    public async Task Valid_synthetic_table_preserves_all_nineteen_columns_and_sparse_rarity_maps()
    {
        using var archive = new BuiltArchive(BuildArchive());
        var source = new PvfEquipmentReinforcementDefinitionSource(archive.Provider);
        var table = await source.GetNormalTableAsync(default);
        Assert.NotNull(table);
        Assert.Single(table.Rows);
        Assert.Equal(19, table.Rows[0].Columns.Count);
        Assert.Equal((1, 10000, 2, 1, 3037, 10), Row(table, 1));
        Assert.Equal(10, table.DestroyLevelByRarity[0]);
        Assert.False(table.DestroyLevelByRarity.ContainsKey(2));
    }

    [Fact]
    public async Task Malformed_equipment_and_protection_action_are_not_usable_definitions()
    {
        using var archive = new BuiltArchive(BuildArchive());
        var source = new PvfEquipmentReinforcementDefinitionSource(archive.Provider);
        Assert.Null(await source.GetEquipmentAsync(9001, default));
        Assert.Null(await source.GetProtectionAsync(9002, default));
        Assert.Null(await source.GetProtectionAsync(9003, default));
        Assert.False((await source.GetEquipmentAsync(9004, default))!.CanReinforce);
    }

    [Fact]
    public async Task Missing_archive_sections_and_empty_item_lists_do_not_supply_definitions()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("etc", "unrelated.etc").Tag("[other]").Int(1).End();
        builder.AddScript("equipment", "equipment.lst").End();
        builder.AddScript("stackable", "stackable.lst").End();
        using var archive = new BuiltArchive(builder);
        var source = new PvfEquipmentReinforcementDefinitionSource(archive.Provider);
        Assert.Null(await source.GetNormalTableAsync(default));
        Assert.Null(await source.GetEquipmentAsync(9001, default));
        Assert.Null(await source.GetProtectionAsync(9002, default));
    }

    private static (int, int, int, int, int, int) Row(
        EquipmentReinforcementTable table, int level)
    {
        var row = table.Rows[level - 1];
        return (row.TargetLevel, row.FailureThreshold, row.PenaltyType,
            row.PenaltyValue, row.MaterialItemId, row.MaterialCount);
    }

    private static PvfArchiveBuilder BuildArchive(string? defect = null)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("etc", "upgrade.etc")
            .Tag("[table]").Tag("[table type]").Quoted("normal");
        for (var index = 0; index < (defect == "short-row" ? 18 : 19); index++)
        {
            if (defect == "nan" && index == 6) script.Single(float.NaN);
            else if (defect == "infinity" && index == 6) script.Single(float.PositiveInfinity);
            else if (defect == "float-failure-threshold" && index == 7) script.Single(10000f);
            else if (index == 7) script.Int(10000);
            else if (index == 8) script.Int(2);
            else if (index == 9) script.Int(1);
            else if (index == 10) script.Int(3037);
            else if (index == 11) script.Int(10);
            else script.Int(index);
        }
        script.Tag("[/table]")
            .Tag("[notice]").Int(12)
            .Tag("[cost weights by rarity]");
        for (var index = 0; index < 7; index++) script.Single(0.5f);
        script.Tag("[type]");
        for (var index = 0; index < 12; index++) script.Single(1.2f);
        script.Tag("[destroy level by rarity]").Quoted("common").Int(10);
        if (defect == "duplicate-rarity") script.Quoted("common").Int(11);
        script.Tag("[max upgrade level by rarity]").Quoted("common").Int(50);
        if (defect != "missing-disjoint")
            script.Tag("[disjoint]")
                .Tag("[disjoint bonus item]").Int(3243)
                .Tag("[equip level const]").Int(40)
                .Tag("[upgrade failed bonus weight by rarity]").Quoted("rare").Single(1.2f)
                .Tag("[upgrade const for bonus item count]").Int(10)
                .Tag("[correction grade by rarity]").Quoted("common").Int(1)
                .Tag("[/disjoint]");
        script.Tag("[cost weight by upgrade level]").Int(0).Single(0.5f);
        if (defect == "odd-cost-weights") script.Int(1);
        if (defect != "missing-cost") script.Tag("[cost]").Int(0).Int(120);
        script.End();

        builder.AddScript("equipment", "equipment.lst")
            .Int(9001).Quoted("invalid.equ")
            .Int(9004).Quoted("blocked.equ").End();
        builder.AddScript("equipment", "invalid.equ")
            .Tag("[grade]").Int(3)
            .Tag("[rarity]").Int(0)
            .Tag("[minimum level]").Int(3)
            .Tag("[equipment type]").Quoted("[weapon]").Int(17)
            .Tag("[durability]").Int(-1).End();
        builder.AddScript("equipment", "blocked.equ")
            .Tag("[grade]").Int(3)
            .Tag("[rarity]").Int(0)
            .Tag("[minimum level]").Int(3)
            .Tag("[equipment type]").Quoted("[weapon]").Int(17)
            .Tag("[durability]").Int(45)
            .Tag("[impossible contents]").Quoted("[upgrade]")
            .Tag("[/impossible contents]").End();
        builder.AddScript("stackable", "stackable.lst")
            .Int(9002).Quoted("invalid.stk")
            .Int(9003).Quoted("unknown.stk").End();
        builder.AddScript("stackable", "invalid.stk")
            .Tag("[action type]").Quoted("[new protect equipment]").Int(-1)
            .Tag("[/action type]").End();
        builder.AddScript("stackable", "unknown.stk")
            .Tag("[action type]").Quoted("[some other action]")
            .Tag("[/action type]").End();
        return builder;
    }

    private sealed class BuiltArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(),
            $"dfo-upgrade-{Guid.NewGuid():N}.pvf");

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
