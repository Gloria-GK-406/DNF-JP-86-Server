using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Data.World;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfHellRewardDefinitionSourceTests
{
    [SkippableTheory]
    [InlineData(75)]
    [InlineData(85)]
    public async Task Real_archive_exposes_reward_rows_actor_gate_and_drawable_pools(int level)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfHellRewardDefinitionSource(provider);
        var content = source.Load();
        Assert.Equal(2, content.Bands.Count);
        Assert.Equal(new[] { 335, 875, 1000, 1400, 1000 }, content.Bands[1].Rates);
        Assert.All(content.RarityThresholds, row => Assert.Equal(7, row.Count));
        Assert.Equal(new ItemGradeBand(68, 77), content.GradeBands[75]);
        Assert.True(source.IsHellMonster(20444, true));
        Assert.False(source.IsHellMonster(440, true));
        Assert.NotEmpty(content.EpicPieces);
        var parties = new PvfHellDungeonDefinitionSource(provider);
        var party = await parties.GetPartyAsync(default);
        Assert.Equal(8, party.ManualDifficulties.Single(x => x.Difficulty == 1).RewardRollCount);
        Assert.Equal(6, party.ManualDifficulties.Single(x => x.Difficulty == 2).RewardRollCount);
        var pools = new PvfDropItemPoolSource(provider);
        var rules = new HellRewardRuleService(source, parties, pools, new());
        var result = await rules.DrawGroupAsync(new(new(DungeonRunId.New(), 77, 123, new(3, 1), 60064,
            DungeonDifficulty.Tier3, 12951, level, 5, new(1123), [new(new(1), new(1123))]), 1, 136, level), default);
        Assert.NotEmpty(result);
        Assert.All(result, drop => Assert.True(drop.ItemId > 0 && drop.Count > 0));
        Assert.Contains(result, drop => drop.Kind == DropItemKind.Equipment);
        if (pools.Load(DropItemFamily.Equipment).Band(content.GradeBands[level], DropRarity.Epic)
            .Any(x => content.EpicPieces.ContainsKey(x.ItemId)))
            Assert.Equal(10, result.Count(x => x.Kind == DropItemKind.Stackable));
        // Every generated template must be usable by the existing inventory pipeline.
        var placement = new PvfItemPlacementDefinitionSource(provider, new Dfo.Server.Abstractions.Inventory.InventorySlotMappingRuleService());
        foreach (var drop in result)
            Assert.NotNull(await placement.GetAsync(drop.ItemId, default));
    }
}
