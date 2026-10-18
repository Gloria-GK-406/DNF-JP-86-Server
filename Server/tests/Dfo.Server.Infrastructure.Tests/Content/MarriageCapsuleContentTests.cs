using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class MarriageCapsuleContentTests
{
    [SkippableFact]
    public async Task Real_archive_supplies_capsule_effect_and_couple_entry_requirements()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason); using var provider=ContentArchive.CreateProvider();
        Assert.Equal(new MarriageCapsuleRules(10,10,2,10,10002910),new PvfMarriageCapsuleContentSource(provider).Load());
        var item=await new PvfExperienceItemDefinitionSource(provider).GetAsync(10002910,default);
        Assert.NotNull(item); Assert.True(item.IsReadable); Assert.Equal(ExperienceItemGrantKind.Fixed,item.GrantKind);
        Assert.Equal(30000u,item.Value); Assert.Equal(30,item.MinimumLevel); Assert.Equal(84,item.MaximumLevel);
        var dungeon=await new PvfDungeonDefinitionSource(provider).GetAsync(3541,default);
        Assert.NotNull(dungeon); Assert.Equal(30,dungeon.MinimumRequiredLevel);Assert.False(dungeon.ConsumesFatigue);
        Assert.Equal(new[]{30,85},dungeon.RecommendedLevels); Assert.Equal(new byte[]{2},dungeon.SupportedDifficulties);
        Assert.Equal(new DungeonRequiredItem(10002609,3,true),Assert.Single(dungeon.RequiredItems));
        Assert.Equal(1,Assert.Single(dungeon.Mazes).Width);Assert.Equal(1,Assert.Single(dungeon.Mazes).Height);
    }
}
