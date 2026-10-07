using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.World;

public sealed class HellRewardRuleServiceTests
{
    [Theory]
    [InlineData(0, DropRarity.Common)]
    [InlineData(599000, DropRarity.Uncommon)]
    [InlineData(599001, DropRarity.Rare)]
    [InlineData(993000, DropRarity.Rare)]
    [InlineData(993001, DropRarity.Epic)]
    [InlineData(1000000, DropRarity.Epic)]
    public void Cumulative_rarity_thresholds_include_their_upper_boundary(int roll, DropRarity expected) =>
        Assert.Equal(expected, HellRewardRules.RarityFor(roll, [0, 599000, 993000, 993000, 1000000, 1000001, 1000001]));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1000, 8)]
    [InlineData(1400, 8)]
    public async Task Equipment_probability_and_attempts_are_separate_from_fragment_policy(int rate, int equipmentCount)
    {
        var rules = new HellRewardRuleService(new Content(rate), new Parties(), new Pool(), new());
        var context = Context(1);
        var drops = await rules.DrawGroupAsync(context, default);
        Assert.Equal(equipmentCount, drops.Count(x => x.Kind == DropItemKind.Equipment));
        Assert.Equal(6, drops.Count(x => x.Kind == DropItemKind.Stackable));
        Assert.All(drops.Where(x => x.Kind == DropItemKind.Equipment), item =>
        {
            Assert.Equal(14400, item.ItemId);
            Assert.Equal((ushort)25, item.Durability);
        });
        Assert.All(drops.Where(x => x.Kind == DropItemKind.Stackable), item => Assert.Equal(10300000, item.ItemId));
        Assert.Equal(drops, await rules.DrawGroupAsync(context, default));
    }

    [Fact]
    public async Task Missing_grade_or_empty_candidate_pool_never_falls_back_to_another_rarity()
    {
        var rules = new HellRewardRuleService(new Content(1000), new Parties(), new EmptyPool(), new());
        Assert.Empty(await rules.DrawGroupAsync(Context(2), default));
        rules = new(new Content(1000), new Parties(), new Pool(), new());
        Assert.Empty(await rules.DrawGroupAsync(Context(1) with { BasisLevel = 201 }, default));
    }

    private static HellGroupRewardContext Context(byte difficulty) => new(new(DungeonRunId.New(), 77, 123,
        new(3, 1), 60064, DungeonDifficulty.Normal, 100, 75, 5, new(10), [new(new(1), new(10))]), difficulty, 136, 75);

    private sealed class Content(int rate) : IHellRewardDefinitionSource
    {
        public HellRewardDefinition Load() => new([new(45, 200, [rate, rate, rate, rate, rate])],
            [new[] { 0, 0, 0, 0, 1000001, 1000001, 1000001 }, new[] { 0, 0, 0, 0, 1000001, 1000001, 1000001 }],
            new Dictionary<int, ItemGradeBand> { [75] = new(68, 77) }, new Dictionary<int, int> { [14400] = 10300000 });
        public bool IsHellMonster(int code, bool isAiCharacter) => false;
    }

    private sealed class Parties : IHellDungeonDefinitionSource
    {
        public ValueTask<HellDungeonDefinition?> GetAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<HellPartyDefinition> GetPartyAsync(CancellationToken ct) =>
            ValueTask.FromResult(new HellPartyDefinition([new(1, 100) { RewardRollCount = 8 }, new(2, 100) { RewardRollCount = 6 }], []));
    }

    private sealed class Pool : IDropItemPoolSource
    {
        public DropItemPool Load(DropItemFamily family) => new(family,
            new Dictionary<(int, int), IReadOnlyList<DropPoolEntry>> { [(75, 4)] = [new(14400, 1000, 25)] });
    }

    private sealed class EmptyPool : IDropItemPoolSource
    {
        public DropItemPool Load(DropItemFamily family) => new(family, new Dictionary<(int, int), IReadOnlyList<DropPoolEntry>>());
    }
}
