using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

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
