using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.World;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfDarkElfRuinsContentSourceTests
{
    [SkippableFact]
    public void Rotation_cost_tiers_and_rewards_stay_bound_to_their_archive_rows()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var catalog = new PvfDarkElfRuinsContentSource(provider).Load();
        Assert.Equal(12, catalog.Dungeons.Count);
        Assert.Equal(2, catalog.DailyEntries);
        Assert.Equal(2, catalog.MonthlyGuardians);
        Assert.Equal(Enumerable.Range(5006, 4), catalog.OpenDungeons[1]);
        Assert.Equal(Enumerable.Range(5016, 4), catalog.OpenDungeons[2]);
        Assert.Equal(Enumerable.Range(5026, 4), catalog.OpenDungeons[3]);
        Assert.Equal(catalog.OpenDungeons[1], catalog.OpenDungeons[4]);
        Assert.Equal(12, catalog.OpenDungeons[0].Distinct().Count());
        Assert.Equal(new byte[] {0, 1, 2, 3}, catalog.Difficulties.OrderBy(x => x.Key).Select(x => x.Value));
        Assert.Equal(new[] {3, 4}, catalog.GuardianLicenses);
        Assert.Equal(new ItemDeliveryGrant(10155143, 4), Assert.Single(catalog.Rewards.Single(x => x.License == 4).Clear));
        Assert.Equal(new ItemDeliveryGrant(10155138, 2), catalog.DailyRewards[5019]);
        Assert.Equal(10, catalog.GuardianRates[0].PerTenThousand);
        Assert.Equal(10000, catalog.GuardianRates.Single(x => x.Entries == 25).PerTenThousand);
        Assert.Empty(catalog.Rewards.Single(x => x.License == 1).GuardianDrops);
        Assert.Equal(12, catalog.Rewards.Single(x => x.License == 3).GuardianDrops.Count);
        Assert.Equal(15, catalog.Rewards.Single(x => x.License == 4).GuardianDrops.Count);
        Assert.Equal(6400, catalog.Rewards.Single(x => x.License == 3).GuardianDrops.Sum(x => x.Weight));
    }

    [SkippableFact]
    public async Task Every_ruins_tier_has_enterable_ordinary_and_declared_guardian_topologies()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var content = new PvfDarkElfRuinsContentSource(provider);
        var dungeons = new PvfDungeonDefinitionSource(provider);
        var rules = new DarkElfRuinsRuleService(content, dungeons);
        var layouts = new DungeonStructureRuleService(dungeons, new PvfDungeonMapDefinitionSource(provider));
        foreach (var entry in content.Load().Dungeons)
        {
            var openDay = content.Load().OpenDungeons.First(x => x.Value.Contains(entry.DungeonId)).Key;
            var rule = await rules.EntryAsync(entry.DungeonId, (DayOfWeek)openDay, default);
            Assert.True(rule.Open);
            Assert.Equal(new DungeonRequiredItem(3033, 10 << (entry.License - 1), true), Assert.Single(rule.Dungeon.RequiredItems));
            foreach (var maze in entry.OrdinaryMazes.Concat(entry.GuardianMazes))
            {
                var facts = DungeonEntryFacts.None with { Difficulty = (DungeonDifficulty)rule.Difficulty, RequiredMazeIndex = maze };
                var layout = await layouts.ResolveAsync(entry.DungeonId, facts, 123, default);
                Assert.NotNull(layout);
                Assert.Equal(maze, layout.MazeIndex);
                Assert.True(layout.StartRoomMapId > 0);
                Assert.False(layout.ConsumesFatigue);
                layout = rules.PrepareLayout(layout, 85, new(new(2026, 10, 27), entry.GuardianMazes.Contains(maze), entry.License));
                if (entry.GuardianMazes.Contains(maze))
                {
                    var warp = Assert.Single(layout.Warps);
                    Assert.True(layout.CanTraverse(warp.Key, warp.Value));
                    Assert.False(layout.CanTraverse(warp.Value, warp.Key));
                    Assert.True((await layouts.SelectMapAsync(layout, warp.Key, warp.Value, default)).MapId > 0);
                    Assert.Contains(layout.RandomObjects, x => x.ObjectId == 57601);
                    Assert.All(layout.RandomObjects, x => { Assert.Equal(1, x.SpawnMode); Assert.Equal(200, x.Team); });
                }
                else
                {
                    Assert.Empty(layout.Warps); Assert.Empty(layout.RandomObjects);
                }
            }
        }
    }
}
