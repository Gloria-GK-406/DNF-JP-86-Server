using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class MarriageCapsuleContentTests
{
    [SkippableFact]
    public async Task All_declared_couple_dungeon_independent_rewards_have_strict_inventory_metadata()
    {
        Skip.IfNot(ContentArchive.Available,ContentArchive.SkipReason);using var provider=ContentArchive.CreateProvider();
        var rows=new PvfIndependentDropTableSource(provider).Load().Rows.Where(x=>x.SourceId is 64760 or 64761 or 64762).ToArray();
        Assert.NotEmpty(rows);var ids=rows.SelectMany(x=>x.PoolMode==0?new[]{x.ItemId}:x.Choices.Select(y=>y.ItemId)).Where(x=>x>0).Distinct().ToArray();
        Assert.Contains(10002915,ids);var items=new PvfItemPlacementDefinitionSource(provider);
        foreach(var id in ids)Assert.NotNull(await items.GetAsync(id,default));
    }
    [SkippableFact]
    public async Task Real_archive_supplies_capsule_effect_and_couple_entry_requirements()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason); using var provider=ContentArchive.CreateProvider();
        Assert.Equal(new MarriageCapsuleRules(10,10,2,10,10002910),new PvfMarriageCapsuleContentSource(provider).Load());
        var item=await new PvfExperienceItemDefinitionSource(provider, options: new Dfo.Server.Infrastructure.Content.PvfContentOptions()).GetAsync(10002910,default);
        Assert.NotNull(item); Assert.True(item.IsReadable); Assert.Equal(ExperienceItemGrantKind.Fixed,item.GrantKind);
        Assert.Equal(30000u,item.Value); Assert.Equal(30,item.MinimumLevel); Assert.Equal(84,item.MaximumLevel);
        var dungeon=await new PvfDungeonDefinitionSource(provider).GetAsync(3541,default);
        Assert.NotNull(dungeon); Assert.Equal(30,dungeon.MinimumRequiredLevel);Assert.False(dungeon.ConsumesFatigue);
        Assert.Equal(new[]{30,85},dungeon.RecommendedLevels); Assert.Equal(new byte[]{2},dungeon.SupportedDifficulties);
        Assert.Equal(new DungeonRequiredItem(10002609,3,true),Assert.Single(dungeon.RequiredItems));
        Assert.Equal(1,Assert.Single(dungeon.Mazes).Width);Assert.Equal(1,Assert.Single(dungeon.Mazes).Height);
    }
}
