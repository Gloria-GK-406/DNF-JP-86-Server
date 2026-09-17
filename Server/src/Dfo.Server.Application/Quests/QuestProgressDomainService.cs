using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Quests;

/// <summary>
/// Routes verified business facts and client reports through the active quests. Protocol callers
/// do not select quest IDs or mutate trigger channels for server-owned facts.
/// </summary>
public sealed class QuestProgressDomainService(
    IQuestDefinitionDomainService definitions,
    ICharacterQuestLogDomainService logs,
    IQuestTriggerDomainService clientReports,
    ISeekingQuestProgressDomainService seeking) : IQuestProgressDomainService
{
    public async ValueTask<QuestProgressReport> ReportAsync(
        AccountId accountId,
        CharacterId characterId,
        IReadOnlyList<QuestProgressFact> facts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.Count == 0)
            return new QuestProgressReport(null, [], null);

        var clientFact = facts.OfType<ClientQuestTriggerReport>().SingleOrDefault();
        if (clientFact is not null && facts.Count != 1)
        {
            throw new ArgumentException(
                "A client trigger report cannot share a projection with server facts.", nameof(facts));
        }

        var log = logs.LogOf(accountId, characterId);
        QuestAdvance? clientResponse = null;
        var changes = new List<QuestProgressChange>();
        var serverProgressChanged = false;
        foreach (var fact in facts)
        {
            switch (fact)
            {
                case ClientQuestTriggerReport report:
                    var definition = await definitions.FindAsync(report.QuestId, cancellationToken)
                        .ConfigureAwait(false);
                    clientResponse = definition is null
                        ? QuestAdvance.Refused
                        : await clientReports.ApplyClientReportAsync(
                            accountId, characterId, definition, report.Selector,
                            report.Increment, cancellationToken).ConfigureAwait(false);
                    break;

                case QuestEnemyDefeated death:
                    var advanced = await AdvanceEnemyQuestsAsync(
                        log, death, cancellationToken).ConfigureAwait(false);
                    changes.AddRange(advanced);
                    serverProgressChanged |= advanced.Count != 0;
                    break;

                case QuestHoldingsChanged:
                    serverProgressChanged |= await seeking.SynchronizeAsync(
                        accountId, characterId, cancellationToken).ConfigureAwait(false) is not null;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(facts), fact, "Unknown quest progress fact.");
            }
        }

        var snapshot = serverProgressChanged
            ? new QuestProgressSnapshot(await log.CarriedAsync(cancellationToken).ConfigureAwait(false))
            : null;
        return new QuestProgressReport(clientResponse, changes, snapshot);
    }

    private async ValueTask<IReadOnlyList<QuestProgressChange>> AdvanceEnemyQuestsAsync(
        IQuestLog log,
        QuestEnemyDefeated death,
        CancellationToken cancellationToken)
    {
        var carried = await log.CarriedAsync(cancellationToken).ConfigureAwait(false);
        var changes = new List<QuestProgressChange>();
        foreach (var active in carried)
        {
            var definition = await definitions.FindAsync(active.QuestId, cancellationToken)
                .ConfigureAwait(false);
            if (definition?.HuntEnemyRequirements is not { Count: > 0 } targets)
                continue;

            var before = new QuestTrigger(unchecked((uint)active.TriggerValue));
            var after = before;
            for (var index = 0; index < Math.Min(targets.Count, QuestTrigger.ChannelCount); index++)
            {
                if (Matches(targets[index], death))
                    after = after.ReplaceChannel(index, after.GetChannel(index) - 1L);
            }

            if (after == before)
                continue;
            if (!await log.RecordProgressAsync(
                    active, unchecked((int)after.PackedValue), cancellationToken)
                .ConfigureAwait(false))
            {
                throw new InvalidDataException(
                    $"Quest {active.QuestId} changed while reporting a verified enemy death.");
            }

            changes.Add(new QuestProgressChange(
                active.QuestId, before.PackedValue, after.PackedValue));
        }

        return changes;
    }

    private static bool Matches(HuntEnemyRequirement target, QuestEnemyDefeated death) =>
        target.DungeonId == death.DungeonId
        && (target.Difficulty < 0 || target.Difficulty == death.Difficulty)
        && target.EnemyCode == death.EnemyCode
        && target.EnemyType == death.EnemyType;
}
