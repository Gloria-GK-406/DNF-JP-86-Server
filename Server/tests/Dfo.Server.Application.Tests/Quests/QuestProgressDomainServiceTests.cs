using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class QuestProgressDomainServiceTests
{
    private static readonly AccountId Account = new(6);
    private static readonly CharacterId Character = new(60);

    [Fact]
    public async Task Client_trigger_changes_only_the_CMD_response_projection()
    {
        var report = new QuestAdvance(QuestAdvanceVerdict.Advanced, new QuestTrigger(0));
        var service = Service(new RecordingQuestLogs(), new FixedClientReport(report));

        var result = await service.ReportAsync(
            Account, Character, [new ClientQuestTriggerReport(1776, 0x10, false)],
            CancellationToken.None);

        Assert.Same(report, result.ClientResponse);
        Assert.Null(result.NotificationSnapshot);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task Verified_enemy_death_updates_matching_active_quests_and_projects_full_state()
    {
        var logs = new RecordingQuestLogs
        {
            Carried =
            {
                [Character.Value] =
                [new CarriedQuest(0, 1776, 1), new CarriedQuest(1, 2000, 7)],
            },
        };
        var service = Service(logs, new FixedClientReport(QuestAdvance.Refused));

        var result = await service.ReportAsync(
            Account, Character,
            [new QuestEnemyDefeated(144, 0, 13099, HuntEnemyTargetTypes.Monster)],
            CancellationToken.None);

        Assert.Null(result.ClientResponse);
        Assert.Equal([new QuestProgressChange(1776, 1, 0)], result.Changes);
        Assert.Equal([(1776, 0), (2000, 7)], result.NotificationSnapshot!.Quests
            .Select(quest => (quest.QuestId, quest.TriggerValue)));
    }

    [Fact]
    public async Task Unrelated_enemy_death_does_not_request_a_notification()
    {
        var logs = new RecordingQuestLogs
        {
            Carried = { [Character.Value] = [new CarriedQuest(0, 1776, 1)] },
        };
        var service = Service(logs, new FixedClientReport(QuestAdvance.Refused));

        var result = await service.ReportAsync(
            Account, Character,
            [new QuestEnemyDefeated(144, 0, 13100, HuntEnemyTargetTypes.Monster)],
            CancellationToken.None);

        Assert.Empty(result.Changes);
        Assert.Null(result.NotificationSnapshot);
    }

    [Fact]
    public async Task Verified_dungeon_clear_completes_only_the_matching_objective_once()
    {
        var logs = new RecordingQuestLogs
        {
            Carried =
            {
                [Character.Value] =
                [new CarriedQuest(0, 1781, 1), new CarriedQuest(1, 1776, 1)],
            },
        };
        var service = Service(logs, new FixedClientReport(QuestAdvance.Refused));

        var unrelated = await service.ReportAsync(
            Account, Character, [new QuestDungeonCleared(145, 0)], default);
        var matched = await service.ReportAsync(
            Account, Character, [new QuestDungeonCleared(146, 0)], default);
        var repeated = await service.ReportAsync(
            Account, Character, [new QuestDungeonCleared(146, 0)], default);

        Assert.Empty(unrelated.Changes);
        Assert.Null(unrelated.NotificationSnapshot);
        Assert.Equal([new QuestProgressChange(1781, 1, 0)], matched.Changes);
        Assert.Equal([(1781, 0), (1776, 1)], matched.NotificationSnapshot!.Quests
            .Select(quest => (quest.QuestId, quest.TriggerValue)));
        Assert.Empty(repeated.Changes);
        Assert.Null(repeated.NotificationSnapshot);
        Assert.Single(logs.ProgressWrites);
    }

    [Fact]
    public async Task Verified_monster_death_counts_only_matching_hunt_monster_channels()
    {
        var logs = new RecordingQuestLogs
        {
            Carried = { [Character.Value] = [new CarriedQuest(0, 2001, 2 | (1 << 9))] },
        };
        var service = Service(logs, new FixedClientReport(QuestAdvance.Refused));

        var result = await service.ReportAsync(
            Account, Character,
            [new QuestEnemyDefeated(144, 2, 13099, HuntEnemyTargetTypes.Monster,
                MonsterRank.Boss)], CancellationToken.None);

        Assert.Equal([new QuestProgressChange(2001, 514, 1)], result.Changes);
        Assert.Equal(1, Assert.Single(result.NotificationSnapshot!.Quests).TriggerValue);
    }

    [Fact]
    public async Task Hunt_monster_wildcard_needs_verified_rank_and_minimum_difficulty()
    {
        var logs = new RecordingQuestLogs
        {
            Carried = { [Character.Value] = [new CarriedQuest(0, 2001, 2 | (1 << 9))] },
        };
        var service = Service(logs, new FixedClientReport(QuestAdvance.Refused));
        foreach (var fact in new[]
        {
            new QuestEnemyDefeated(145, 2, 13099, HuntEnemyTargetTypes.Monster,
                MonsterRank.Boss),
            new QuestEnemyDefeated(144, 0, 13099, HuntEnemyTargetTypes.Monster,
                MonsterRank.Boss),
            new QuestEnemyDefeated(144, 2, 13100, HuntEnemyTargetTypes.Monster),
        })
        {
            var result = await service.ReportAsync(Account, Character, [fact],
                CancellationToken.None);
            Assert.Empty(result.Changes);
            Assert.Null(result.NotificationSnapshot);
        }
    }

    [Fact]
    public async Task Cleared_quest_fact_advances_only_dependents_and_is_idempotent()
    {
        var logs = new RecordingQuestLogs
        {
            Carried = { [Character.Value] = [new CarriedQuest(0, 2002, 1)] },
        };
        var service = Service(logs, new FixedClientReport(QuestAdvance.Refused));

        var unrelated = await service.ReportAsync(
            Account, Character, [new QuestCleared(101)], CancellationToken.None);
        logs.Cleared[Character.Value] = [new ClearedQuest(1776, 1)];
        var matched = await service.ReportAsync(
            Account, Character, [new QuestCleared(1776)], CancellationToken.None);
        var duplicate = await service.ReportAsync(
            Account, Character, [new QuestCleared(1776)], CancellationToken.None);

        Assert.Null(unrelated.NotificationSnapshot);
        Assert.Equal([new QuestProgressChange(2002, 1, 0)], matched.Changes);
        Assert.Equal(0, Assert.Single(matched.NotificationSnapshot!.Quests).TriggerValue);
        Assert.Null(duplicate.NotificationSnapshot);
    }

    [Fact]
    public async Task Holdings_change_recomputes_progress_and_requests_one_private_snapshot()
    {
        var logs = new RecordingQuestLogs
        {
            Carried = { [Character.Value] = [new CarriedQuest(0, 1779, 1)] },
        };
        var service = Service(
            logs, new FixedClientReport(QuestAdvance.Refused),
            new ChangingSeekingProgress(logs));

        var result = await service.ReportAsync(
            Account, Character, [new QuestHoldingsChanged()], CancellationToken.None);

        Assert.Null(result.ClientResponse);
        Assert.Equal(0, Assert.Single(result.NotificationSnapshot!.Quests).TriggerValue);
        Assert.Single(logs.ProgressWrites);
    }

    private static QuestProgressDomainService Service(
        RecordingQuestLogs logs,
        IQuestTriggerDomainService clientReports,
        ISeekingQuestProgressDomainService? seeking = null) =>
        new(new FixedDefinitions(), logs, clientReports, seeking ?? new NoSeekingProgress());

    private sealed class FixedDefinitions : IQuestDefinitionDomainService
    {
        public ValueTask<QuestDefinition?> FindAsync(
            int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<QuestDefinition?>(questId == 1776
                ? new QuestDefinition(
                    1776, "", null, null, null, null, null, null, [], "hunt enemy", "",
                    null, null, null, null, null, null, null, [], [], [], [],
                    HuntEnemyRequirements:
                    [new HuntEnemyRequirement(
                        144, -1, 13099, HuntEnemyTargetTypes.Monster, 1)])
                : questId == 2001
                ? new QuestDefinition(
                    2001, "", null, null, null, null, null, null, [], "hunt monster", "",
                    null, null, null, null, null, null, null, [], [], [], [],
                    HuntMonsterRequirements:
                    [new HuntMonsterRequirement(144, 1, 13099, 2),
                     new HuntMonsterRequirement(144, 1, -3, 1)])
                : questId == 2002
                ? new QuestDefinition(
                    2002, "", null, null, null, null, null, null, [], "clear quest", "",
                    null, null, null, null, null, null, null, [], [], [], [],
                    ClearedQuestRequirements: [1776])
                : questId == 1781
                ? new QuestDefinition(
                    1781, "", null, null, null, null, null, null, [],
                    "condition under clear", "", null, null, null, null, null, null,
                    null, [], [], [], [],
                    DungeonClearRequirement: new DungeonClearRequirement(146, -1))
                : null);

        public QuestParameters RewardParameters() => throw new NotSupportedException();

        public QuestRewardResolution ResolveRewards(
            QuestDefinition definition, QuestRewardCandidate candidate,
            ushort rewardSelection, ushort completionCount) =>
            throw new NotSupportedException();
    }

    private sealed class FixedClientReport(QuestAdvance response) : IQuestTriggerDomainService
    {
        public ValueTask<QuestAdvance> ApplyClientReportAsync(
            AccountId accountId, CharacterId characterId, QuestDefinition definition,
            byte selector, bool increment, CancellationToken cancellationToken) =>
            ValueTask.FromResult(response);
    }

    private sealed class NoSeekingProgress : ISeekingQuestProgressDomainService
    {
        public ValueTask<SeekingQuestProgressSynchronization?> SynchronizeAsync(
            AccountId accountId, CharacterId characterId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<SeekingQuestProgressSynchronization?>(null);
    }

    private sealed class ChangingSeekingProgress(RecordingQuestLogs logs)
        : ISeekingQuestProgressDomainService
    {
        public async ValueTask<SeekingQuestProgressSynchronization?> SynchronizeAsync(
            AccountId accountId, CharacterId characterId,
            CancellationToken cancellationToken)
        {
            var log = logs.LogOf(accountId, characterId);
            var before = Assert.Single(await log.CarriedAsync(cancellationToken));
            Assert.True(await log.RecordProgressAsync(before, 0, cancellationToken));
            return new SeekingQuestProgressSynchronization(
                await log.CarriedAsync(cancellationToken));
        }
    }
}
