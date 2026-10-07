using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonSettlementRuleServiceTests
{
    private static readonly AccountId Account = new(1);
    private static readonly CharacterId Character = new(42);

    [Fact]
    public async Task OrdinaryRunRequiresVerifiedClear()
    {
        var rules = new DungeonSettlementRuleService();

        var result = await rules.ForAsync(
            Account, Character, Run(DungeonRunPurpose.Ordinary), default);

        Assert.False(result.CanComplete);
        Assert.False(result.QuestMaze);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task QuestRunSettlementDependsOnVerifiedClearNotQuestTrigger(
        bool clearOpen,
        bool expected)
    {
        var rules = new DungeonSettlementRuleService();

        var result = await rules.ForAsync(
            Account,
            Character,
            Run(DungeonRunPurpose.QuestLinked(1776, DungeonQuestConnectionState.Active))
                with { ClearOpen = clearOpen },
            default);

        Assert.Equal(expected, result.CanComplete);
        Assert.True(result.QuestMaze);
    }

    [Fact]
    public async Task ClearedQuestRunUsesTheSameClearFact()
    {
        var rules = new DungeonSettlementRuleService();

        var result = await rules.ForAsync(
            Account,
            Character,
            Run(DungeonRunPurpose.QuestLinked(1776, DungeonQuestConnectionState.Cleared))
                with { ClearOpen = true },
            default);

        Assert.True(result.CanComplete);
        Assert.True(result.QuestMaze);
    }

    private static DungeonRunState Run(DungeonRunPurpose purpose)
    {
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(
            144,
            1,
            1,
            1,
            cell,
            cell,
            55612,
            7,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(55612)},
            Purpose: purpose);
        return new DungeonRunState(
            layout,
            cell,
            55612,
            ImmutableHashSet.Create(cell),
            DateTimeOffset.UtcNow,
            CurrentCellState: DungeonCellSeed.CompleteEmpty(cell, 55612).Snapshot());
    }
}
