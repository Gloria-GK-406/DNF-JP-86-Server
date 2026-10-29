using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.World;

[Trait("Category", "Unit")]
public sealed class LostLandDropRuleTests
{
    [Theory]
    [InlineData(150)]
    [InlineData(151)]
    public async Task Forest_conflict_teams_receive_red_crystals_only_in_the_forest(int team)
    {
        var rules = new LostLandDropRuleService(new Content());
        var actor = new RoomActorDescription(new(69586, 35, 1)
        {
            Declaration = new(69586, DungeonActorSpawnSource.Monster, 0) { MonsterTeam = team },
        }, false);
        var plan = await rules.PrepareAsync(Context(17, 1), actor, 1, default);
        Assert.InRange(Assert.Single(plan.Regular, item => item.ItemId == 10092845).Count, 3, 5);
        Assert.Null(plan.Bonus);
        foreach (var region in new[] { 0, 2, 3, 4, 5 })
            Assert.Empty((await rules.PrepareAsync(Context(17, region), actor, 1, default)).Regular);
        Assert.Empty((await rules.PrepareAsync(Context(17, 1),
            actor with { Actor = actor.Actor with { IsHostile = false } }, 1, default)).Regular);
        Assert.Empty((await rules.PrepareAsync(Context(17, 1),
            actor with { Actor = actor.Actor with { Declaration = actor.Actor.Declaration! with { MonsterTeam = 200 } } },
            1, default)).Regular);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(0, 2)]
    public async Task Ordinary_and_elite_rewards_have_the_approved_bounds_and_probabilities(byte rank, int tier)
    {
        var rules = new LostLandDropRuleService(new Content());
        var successes = 0;
        var rainbows = 0;
        for (var seed = 0; seed < 10000; seed++)
        {
            var context = Context(seed, 2);
            var actor = new RoomActorDescription(new(12345, 50, rank), false) { IsNamedMonster = rank == 0 && tier == 2 };
            var plan = await rules.PrepareAsync(context, actor, 17, default);
            Assert.Null(plan.Bonus);
            Assert.All(plan.Regular, item => Assert.Contains(item.ItemId, new[] { 10092846, 10092849 }));
            var regional = plan.Regular.Where(item => item.ItemId == 10092846).ToArray();
            if (regional.Length != 0)
            {
                successes++;
                Assert.InRange(Assert.Single(regional).Count, tier == 0 ? 1 : tier == 1 ? 3 : 6,
                    tier == 0 ? 3 : tier == 1 ? 5 : 8);
            }
            var rainbow = plan.Regular.Where(item => item.ItemId == 10092849).ToArray();
            if (rainbow.Length != 0)
            {
                rainbows++;
                Assert.InRange(Assert.Single(rainbow).Count, 1, tier == 2 ? 2 : 1);
            }
            var repeated = await rules.PrepareAsync(context, actor, 17, default);
            Assert.Equal(plan.Regular.ToArray(), repeated.Regular.ToArray());
        }
        Assert.InRange(successes, tier == 0 ? 4300 : 10000, tier == 0 ? 4700 : 10000);
        Assert.InRange(rainbows, tier == 0 ? 0 : tier == 1 ? 800 : 2300,
            tier == 0 ? 0 : tier == 1 ? 1200 : 2700);
    }

    [Theory]
    [InlineData(69646, 0)]
    [InlineData(69584, 1)]
    [InlineData(69591, 1)]
    [InlineData(69625, 2)]
    [InlineData(69634, 2)]
    [InlineData(69660, 3)]
    [InlineData(69619, 4)]
    [InlineData(69667, 5)]
    [InlineData(69668, 5)]
    [InlineData(69669, 5)]
    public async Task Bosses_freeze_guarantees_and_share_their_region_or_tower_family(int code, int region)
    {
        var rules = new LostLandDropRuleService(new Content());
        var bonusBags = 0;
        var equipment = 0;
        for (var seed = 0; seed < 1000; seed++)
        {
            var plan = await rules.PrepareAsync(Context(seed, region), new(new(code, 86, 3), false), 19, default);
            var bonus = Assert.IsType<LostLandKillBonus>(plan.Bonus);
            Assert.Contains(code, bonus.SharedBossCodes);
            Assert.InRange(Assert.Single(bonus.Items, item => item.ItemId == 10092849).Count,
                region == 5 ? 40 : 8, region == 5 ? 60 : 12);
            if (region == 5)
            {
                Assert.Empty(plan.Regular);
                Assert.Equal(3, Assert.Single(bonus.Items, item => item.ItemId == 10092856).Count);
                Assert.Equal(new[] {69667, 69668, 69669}, bonus.SharedBossCodes.Order().ToArray());
                foreach (var item in bonus.Items.Where(item => item.Kind == DropItemKind.Equipment))
                {
                    equipment++;
                    Assert.Equal(60, item.Durability);
                    Assert.Equal(90000, item.ItemId);
                }
            }
            else
            {
                Assert.InRange(Assert.Single(plan.Regular).Count, 20, 30);
                Assert.Equal(10092844 + region, plan.Regular[0].ItemId);
                if (bonus.Items.Any(item => item.ItemId == 10092856)) bonusBags++;
                if (region == 1) Assert.Equal(new[] {69584, 69591}, bonus.SharedBossCodes.Order().ToArray());
                if (region == 2) Assert.Equal(new[] {69625, 69634}, bonus.SharedBossCodes.Order().ToArray());
            }
        }
        if (region == 5) Assert.InRange(equipment, 150, 250);
        else Assert.InRange(bonusBags, 190, 310);
    }

    [Fact]
    public async Task Chaser_uses_its_own_reward_without_an_ordinary_or_regional_boss_roll()
    {
        var rules = new LostLandDropRuleService(new Content());
        var bags = 0;
        for (var seed = 0; seed < 1000; seed++)
        {
            var plan = await rules.PrepareAsync(Context(seed, 0), new(new(59822, 86, 3), false), 5, default);
            Assert.Null(plan.Bonus);
            Assert.InRange(Assert.Single(plan.Regular, item => item.ItemId == 10092849).Count, 3, 5);
            Assert.DoesNotContain(plan.Regular, item => item.ItemId is >= 10092844 and <= 10092848);
            if (plan.Regular.Any(item => item.ItemId == 10092856)) bags++;
        }
        Assert.InRange(bags, 430, 570);
    }

    [Fact]
    public async Task Non_activity_allied_dummy_and_APC_actors_receive_no_activity_rewards()
    {
        var rules = new LostLandDropRuleService(new Content());
        var context = Context(17, 0);
        Assert.Empty((await rules.PrepareAsync(context with { Layout = context.Layout with { DungeonId = 145 } },
            new(new(12345, 50, 0), false), 1, default)).Regular);
        foreach (var actor in new RoomActorDescription[]
        {
            new(new(12345, 50, 0) { IsHostile = false }, false),
            new(new(12345, 50, 5), true),
            new(new(12345, 50, 0) { Declaration = new(12345, DungeonActorSpawnSource.Monster, 0) { IsDummy = true } }, false),
            new(new(12345, 50, 0), false) { IsHellTemplate = true },
        })
        {
            var plan = await rules.PrepareAsync(context, actor, 1, default);
            Assert.Empty(plan.Regular);
            Assert.Null(plan.Bonus);
        }
    }

    internal static DungeonCellCreationContext Context(int seed, int region)
    {
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(4000, 0, 1, 1, cell, cell, 1000 + region, seed,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(1000 + region) })
        { Mode = new DungeonRunMode.LostLand(new(1200, 1200, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), [], [])) };
        return new(DungeonRunId.New(), layout, cell, 1000 + region, 0, DateTimeOffset.UnixEpoch,
            [new(new(1), new(71), 0, true)]);
    }

    internal sealed class Content : ILostLandDropContentSource
    {
        public ValueTask<LostLandDropContent> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new LostLandDropContent(
            Enumerable.Range(0, 6).ToImmutableDictionary(region => 1000 + region, region => region == 5 ? 0 : 10092844 + region),
            Enumerable.Range(10092844, 6).Append(10092856).ToImmutableDictionary(id => id,
                id => new RolledDrop(id, 1, DropRarity.Rare, DropItemKind.Stackable)),
            [new(90000, 1, DropRarity.Unique, DropItemKind.Equipment, 60)]));
    }
}
