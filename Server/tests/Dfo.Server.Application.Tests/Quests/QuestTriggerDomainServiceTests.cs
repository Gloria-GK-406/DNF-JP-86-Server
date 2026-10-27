using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class QuestTriggerDomainServiceTests
{
    private static readonly AccountId Account = new(6);
    private static readonly CharacterId Character = new(60);

    [Theory]
    [InlineData(87, 15262, DungeonQuestConnectionState.Active, -1, 0x10, true)]
    [InlineData(87, 15263, DungeonQuestConnectionState.Active, -1, 0x10, false)]
    [InlineData(88, 15262, DungeonQuestConnectionState.Active, -1, 0x10, false)]
    [InlineData(87, 15262, DungeonQuestConnectionState.Cleared, -1, 0x10, false)]
    [InlineData(87, 15262, DungeonQuestConnectionState.Active, 1, 0x10, false)]
    [InlineData(87, 15262, DungeonQuestConnectionState.Active, -1, 0x20, false)]
    [InlineData(0, 15262, DungeonQuestConnectionState.Active, -1, 0x10, false)]
    public async Task CyclePassiveObjectReportPersistsOnlyForItsBoundActiveQuest(
        int dungeonId, int boundQuestId, DungeonQuestConnectionState state,
        int requiredDifficulty, byte selector, bool accepted)
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new ShopFixture();
            fixture.Sql("INSERT INTO character_active_quests VALUES(1,0,15262,1)");
            var runs = new DungeonRunDomainService();
            if (dungeonId > 0)
                runs.Begin(fixture.Session.CharacterId,
                    Layout(new(DungeonRunPurposeKind.Cycle, boundQuestId, state)) with { DungeonId = dungeonId },
                    difficulty: (Dfo.Server.Data.World.DungeonDifficulty)2);
            var quest = Quest1776() with
            {
                QuestId = 15262,
                Grade = QuestGrade.Circle,
                KillObjectives = QuestKillObjectiveParser.Parse("hunt enemy",
                    [87, requiredDifficulty, 10199, HuntEnemyTargetTypes.PassiveObject, 1]),
            };
            var service = new QuestTriggerDomainService(
                fixture.Services.GetRequiredService<ICharacterQuestLogDomainService>(), runs);
            QuestAdvance result;
            await using (var transaction = ((DomainTransactionScope)fixture.Services
                .GetRequiredService<IDomainTransactionAccessor>()).Begin())
            {
                result = await service.ApplyClientReportAsync(fixture.Session.AccountId,
                    fixture.Session.CharacterId, quest, selector, increment: false, CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
            }

            Assert.Equal(accepted ? QuestAdvanceVerdict.Advanced : QuestAdvanceVerdict.Refused, result.Verdict);
            Assert.Equal(accepted ? 0 : 1,
                fixture.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=15262"));
            Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE quest_id=15262"));
        });
    }

    [Fact]
    public async Task Quest1776PassiveObjectReportIsAcceptedInItsActiveStoryRun()
    {
        await TestGameplay.RunAsync(async () =>
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
        });
    }

    [Fact]
    public async Task TheSameReportIsRefusedInAnOrdinaryRun()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new DungeonRunDomainService();
            runs.Begin(Character, Layout(DungeonRunPurpose.Ordinary), difficulty: 0);
            var logs = new RecordingLogs();
            var service = new QuestTriggerDomainService(logs, runs);

            var result = await service.ApplyClientReportAsync(
                Account, Character, Quest1776(), 0x10, increment: false, CancellationToken.None);

            Assert.Equal(QuestAdvanceVerdict.Refused, result.Verdict);
            Assert.Equal(0, logs.AdvanceCalls);
        });
    }

    private static QuestDefinition Quest1776() =>
        new(1776, "", null, null, null, null, null, null, [], "hunt enemy", "",
            null, null, null, null, null, null, null, [], [], [], [],
            InitialTrigger: 1,
            KillObjectives: QuestKillObjectiveParser.Parse("hunt enemy", [144, -1, 13099, HuntEnemyTargetTypes.PassiveObject, 1]));

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
            new Dictionary<MazeCell, DungeonCellDefinition> {
                [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(1),
                [new MazeCell(1, 0)] = DungeonCellDefinition.Fixed(2),
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
            public ValueTask<IReadOnlyList<int>> CompleteWithoutRewardsAsync(
                IReadOnlyCollection<int> questIds, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public ValueTask<bool> GiveUpAsync(int questId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public ValueTask TrackAsync(IReadOnlyList<int> questIds, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }
    }
}
