using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class QuestTriggerDomainServiceTests
{
    private static readonly AccountId Account = new(6);
    private static readonly CharacterId Character = new(60);

    [Fact]
    public async Task Quest1776PassiveObjectReportIsAcceptedInItsActiveStoryRun()
    {
        var runs = new DungeonRunDomainService();
        runs.Begin(Character, Layout(DungeonRunPurpose.QuestLinked(
            1776, DungeonQuestConnectionState.Active)), difficulty: 0);
        var logs = new RecordingLogs();
        var service = new QuestTriggerDomainService(logs, runs);

        var result = await service.ApplyClientReportAsync(
            Account, Character, Quest1776(), 0x10, increment: false, CancellationToken.None);

        Assert.Equal(QuestAdvanceVerdict.Advanced, result.Verdict);
        Assert.Equal(1, logs.AdvanceCalls);
    }

    [Fact]
    public async Task TheSameReportIsRefusedInAnOrdinaryRun()
    {
        var runs = new DungeonRunDomainService();
        runs.Begin(Character, Layout(DungeonRunPurpose.Ordinary), difficulty: 0);
        var logs = new RecordingLogs();
        var service = new QuestTriggerDomainService(logs, runs);

        var result = await service.ApplyClientReportAsync(
            Account, Character, Quest1776(), 0x10, increment: false, CancellationToken.None);

        Assert.Equal(QuestAdvanceVerdict.Refused, result.Verdict);
        Assert.Equal(0, logs.AdvanceCalls);
    }

    private static QuestDefinition Quest1776() =>
        new(1776, "", null, null, null, null, null, null, [], "hunt enemy", "",
            null, null, null, null, null, null, null, [], [], [], [],
            InitialTrigger: 1,
            HuntEnemyRequirements:
            [
                new HuntEnemyRequirement(
                    144, -1, 13099, HuntEnemyTargetTypes.PassiveObject, 1),
            ]);

    private static DungeonLayout Layout(DungeonRunPurpose purpose) =>
        new(
            144,
            1,
            2,
            1,
            new MazeCell(0, 0),
            new MazeCell(1, 0),
            1,
            42,
            new Dictionary<MazeCell, int>
            {
                [new MazeCell(0, 0)] = 1,
                [new MazeCell(1, 0)] = 2,
            },
            Purpose: purpose);

    private sealed class RecordingLogs : ICharacterQuestLogDomainService
    {
        public int AdvanceCalls { get; private set; }

        public IQuestLog LogOf(AccountId accountId, CharacterId characterId)
        {
            Assert.Equal(Account, accountId);
            Assert.Equal(Character, characterId);
            return new Log(this);
        }

        private sealed class Log(RecordingLogs owner) : IQuestLog
        {
            public CharacterId CharacterId => Character;

            public ValueTask<QuestAdvance> AdvanceAsync(
                QuestDefinition definition,
                byte selector,
                bool increment,
                CancellationToken cancellationToken)
            {
                owner.AdvanceCalls++;
                return ValueTask.FromResult(
                    new QuestAdvance(QuestAdvanceVerdict.Advanced, new QuestTrigger(0)));
            }

            public ValueTask<IReadOnlyList<CarriedQuest>> CarriedAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public ValueTask<CarriedQuest?> FindCarriedAsync(int questId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public ValueTask<IReadOnlyList<ClearedQuest>> ClearedAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public ValueTask<IReadOnlyList<int>> TrackedAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public ValueTask<QuestAcceptance> AcceptAsync(QuestDefinition definition, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public ValueTask<bool> RecordProgressAsync(CarriedQuest asLastSeen, int triggerValue, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public ValueTask<bool> CompleteAsync(int questId, byte mark, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public ValueTask<QuestFinishVerdict> FinishAsync(int questId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public ValueTask<bool> GiveUpAsync(int questId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public ValueTask TrackAsync(IReadOnlyList<int> questIds, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }
    }
}
