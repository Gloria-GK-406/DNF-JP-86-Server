using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;
namespace Dfo.Server.Infrastructure.Tests.Content;
public sealed class PvfExtraDropContentSourceTests
{
    [Fact]
    public void Missing_mask_does_not_mean_all_and_all_expands_without_duplicates()
    {
        string[] allowed = ["[easy]", "[medium]", "[hard]", "[ultimate]", "[hero]"];
        Assert.Empty(PvfExtraDropContentSource.ParseMask([], allowed));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, PvfExtraDropContentSource.ParseMask(["[easy]", "[all]"], allowed));
        Assert.Equal(new[] { 1, 3 }, PvfExtraDropContentSource.ParseMask(["[ultimate]", "[medium]"], allowed));
        Assert.Throws<InvalidDataException>(() => PvfExtraDropContentSource.ParseMask(["[typo]"], allowed));
    }
    [Fact]
    public void Tagged_script_builds_only_its_eligible_buckets_and_missing_item_fails()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(7001).Quoted("test.equ").End();
        builder.AddScript("stackable", "stackable.lst").Int(1000).Quoted("normal.stk").End();
        builder.AddScript("stackable", "normal.stk").Tag("[creation rate]").Int(100).End();
        builder.AddScript("dungeon", "dungeon.lst").Int(144).Quoted("test.dgn").End();
        builder.AddScript("dungeon", "test.dgn").Tag("[ancient dungeon]").Int(1).End();
        builder.AddScript("equipment", "test.equ")
            .Tag("[special monster drop]").Int(1).Tag("[grade]").Int(20)
            .Tag("[rarity]").Int(2).Tag("[creation rate]").Int(100).Tag("[durability]").Int(35)
            .Tag("[difficulty for extra drop]").Quoted("[easy]").Quoted("[hard]")
            .Tag("[dungeon type for extra drop]").Quoted("[ancient]").End();
        using var archive = new SyntheticArchive(builder);
        var content = PvfExtraDropContentSource.Read(archive.Archive);
        Assert.Equal(1, content.ItemCount);
        Assert.Equal(2, content.Pools.Count);
        Assert.Equal(ExtraDropDungeonType.Ancient, content.Dungeons[144]);
        var pool = content.Pools[(DropItemKind.Equipment, 2, ExtraDropDungeonType.Ancient)];
        Assert.Equal(new DropPoolEntry(7001, 100, 35), Assert.Single(pool.Bucket(20, DropRarity.Rare)));
        var missing = new PvfArchiveBuilder();
        missing.AddScript("equipment", "equipment.lst").Int(99).Quoted("missing.equ").End();
        missing.AddScript("stackable", "stackable.lst").Int(1000).Quoted("normal.stk").End();
        using var broken = new SyntheticArchive(missing);
        Assert.Throws<InvalidDataException>(() => PvfExtraDropContentSource.Read(broken.Archive));
    }
    [SkippableFact]
    public void A21_extra_table_and_empty_tagged_pool_initialize_and_are_cached()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfExtraDropContentSource(provider);
        var content = source.Load();
        Assert.Same(content, source.Load());
        Assert.Equal(0, content.ItemCount);
        Assert.Empty(content.Pools);
        Assert.Equal(1047, content.Dungeons.Count);
        var rates = new PvfDropRateTableSource(provider).Load(DropRateTables.MonsterExtra);
        Assert.Equal(7, rates.LevelBands.Count);
        Assert.Equal(4, rates.RarityLadders.Count);
    }
}
