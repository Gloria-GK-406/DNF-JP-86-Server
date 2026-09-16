using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonSettlementRuleServiceTests
{
    private static readonly AccountId Account = new(1);
    private static readonly CharacterId Character = new(42);

    [Fact]
    public async Task OrdinaryRunsCanCompleteWithoutReadingQuestState()
    {
        var rules = new DungeonSettlementRuleService(new RecordingQuestLogs());

        var result = await rules.ForAsync(
            Account, Character, Run(DungeonRunPurpose.Ordinary), default);

        Assert.True(result.CanComplete);
        Assert.False(result.QuestMaze);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task ActiveQuestRunsRequireTheirFrozenQuestTargetToBeComplete(
        int trigger,
        bool expected)
    {
        var logs = new RecordingQuestLogs();
        logs.Carried[Character.Value] = [new CarriedQuest(0, 1776, trigger)];
        var rules = new DungeonSettlementRuleService(logs);

        var result = await rules.ForAsync(
            Account,
            Character,
            Run(DungeonRunPurpose.QuestLinked(1776, DungeonQuestConnectionState.Active)),
            default);

        Assert.Equal(expected, result.CanComplete);
        Assert.True(result.QuestMaze);
    }

    [Fact]
    public async Task ClearedQuestRunsRequireTheFrozenQuestInTheClearedLog()
    {
        var logs = new RecordingQuestLogs();
        logs.Cleared[Character.Value] = [new ClearedQuest(1776, IQuestLog.ClearedMark)];
        var rules = new DungeonSettlementRuleService(logs);

        var result = await rules.ForAsync(
            Account,
            Character,
            Run(DungeonRunPurpose.QuestLinked(1776, DungeonQuestConnectionState.Cleared)),
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
            new Dictionary<MazeCell, int> { [cell] = 55612 },
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
