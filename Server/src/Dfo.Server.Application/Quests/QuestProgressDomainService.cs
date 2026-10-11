using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Quests;

/// <summary>
/// Routes verified business facts and client reports through the active quests. Protocol callers
/// do not select quest IDs or mutate trigger channels for server-owned facts.
/// </summary>
public sealed class QuestProgressDomainService(
    IQuestDefinitionDomainService definitions,
    ICharacterQuestLogDomainService logs,
    IQuestTriggerDomainService clientReports,
    ISeekingQuestProgressDomainService seeking, IMirrorAradQuestDomainService? mirror = null) : IQuestProgressDomainService
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
        var clearedInReport = facts.OfType<QuestCleared>()
            .Select(fact => fact.QuestId).ToHashSet();
        var clearedProcessed = false;
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
                    clientResponse = definition?.Grade == QuestGrade.Crack
                        ? mirror is null ? QuestAdvance.Refused : await mirror.TriggerAsync(accountId, characterId, checked((ushort)report.QuestId), cancellationToken).ConfigureAwait(false)
                        : definition is null ? QuestAdvance.Refused
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

                case QuestDungeonCleared dungeonClear:
                    var clearAdvances = await AdvanceDungeonClearQuestsAsync(
                        log, dungeonClear, cancellationToken).ConfigureAwait(false);
                    changes.AddRange(clearAdvances);
                    serverProgressChanged |= clearAdvances.Count != 0;
                    break;

                case QuestHoldingsChanged:
                    serverProgressChanged |= await seeking.SynchronizeAsync(
                        accountId, characterId, cancellationToken).ConfigureAwait(false) is not null;
                    break;

                case QuestCleared:
                    if (clearedProcessed)
                        break;
                    clearedProcessed = true;
                    var dependents = await AdvanceClearedQuestDependentsAsync(
                        log, clearedInReport, cancellationToken).ConfigureAwait(false);
                    changes.AddRange(dependents);
                    serverProgressChanged |= dependents.Count != 0;
                    break;

                case QuestRoomCleared:
                case QuestCharacterRevived:
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(facts), fact, "Unknown quest progress fact.");
            }
        }

        if (mirror is not null && clientFact is null)
            serverProgressChanged |= await mirror.ReportAsync(accountId, characterId, facts, cancellationToken).ConfigureAwait(false);
        var snapshot = serverProgressChanged
            ? new QuestProgressSnapshot(await log.CarriedAsync(cancellationToken).ConfigureAwait(false))
            : null;
        if (snapshot is not null && mirror is not null)
            snapshot = await mirror.MergeAsync(characterId, snapshot, cancellationToken).ConfigureAwait(false);
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
            if (definition?.Grade == QuestGrade.Crack) continue;
            if (definition?.KillObjectives is not { Status: QuestKillParseStatus.Parsed } objectives)
                continue;
            var targets = objectives.Targets;

            var before = new QuestTrigger(unchecked((uint)active.TriggerValue));
            var after = before;
            foreach (var target in targets)
            {
                if (QuestKillObjectiveRules.Matches(target, death))
                    after = after.ReplaceChannel(target.ChannelIndex, after.GetChannel(target.ChannelIndex) - 1L);
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

    private async ValueTask<IReadOnlyList<QuestProgressChange>> AdvanceDungeonClearQuestsAsync(
        IQuestLog log,
        QuestDungeonCleared clear,
        CancellationToken cancellationToken)
    {
        var carried = await log.CarriedAsync(cancellationToken).ConfigureAwait(false);
        var changes = new List<QuestProgressChange>();
        foreach (var active in carried)
        {
            var definition = await definitions.FindAsync(active.QuestId, cancellationToken)
                .ConfigureAwait(false);
            if (definition?.Grade == QuestGrade.Crack) continue;
            var target = definition?.DungeonClearRequirement;
            if (target is null || target.DungeonId != clear.DungeonId
                || (target.Difficulty >= 0 && target.Difficulty != (int)clear.Difficulty))
                continue;

            var before = new QuestTrigger(unchecked((uint)active.TriggerValue));
            var after = before.ReplaceChannel(0, 0);
            if (after == before)
                continue;
            if (!await log.RecordProgressAsync(active, unchecked((int)after.PackedValue),
                    cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(
                    $"Quest {active.QuestId} changed while reporting a verified dungeon clear.");

            changes.Add(new QuestProgressChange(active.QuestId, before.PackedValue,
                after.PackedValue));
        }

        return changes;
    }

    private async ValueTask<IReadOnlyList<QuestProgressChange>> AdvanceClearedQuestDependentsAsync(
        IQuestLog log,
        IReadOnlySet<int> clearedInReport,
        CancellationToken cancellationToken)
    {
        var carried = await log.CarriedAsync(cancellationToken).ConfigureAwait(false);
        var clearedIds = (await log.ClearedAsync(cancellationToken).ConfigureAwait(false))
            .Where(quest => quest.Mark != 0)
            .Select(quest => quest.QuestId)
            .ToHashSet();
        var changes = new List<QuestProgressChange>();
        foreach (var active in carried)
        {
            var definition = await definitions.FindAsync(active.QuestId, cancellationToken)
                .ConfigureAwait(false);
            if (definition?.ClearedQuestRequirements is not { Count: > 0 } requirements
                || !requirements.Any(clearedInReport.Contains))
                continue;

            var before = new QuestTrigger(unchecked((uint)active.TriggerValue));
            var after = new QuestTrigger(unchecked((uint)QuestStateRuleService.ApplyClearedRequirements(
                definition!, active.TriggerValue, clearedIds)));
            if (after == before)
                continue;
            if (!await log.RecordProgressAsync(
                    active, unchecked((int)after.PackedValue), cancellationToken)
                .ConfigureAwait(false))
            {
                throw new InvalidDataException(
                    $"Quest {active.QuestId} changed while reporting a cleared quest.");
            }

            changes.Add(new QuestProgressChange(active.QuestId, before.PackedValue,
                after.PackedValue));
        }

        return changes;
    }
}
