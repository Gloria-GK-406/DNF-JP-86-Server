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
                    var monsterAdvanced = await AdvanceMonsterQuestsAsync(
                        log, death, cancellationToken).ConfigureAwait(false);
                    changes.AddRange(monsterAdvanced);
                    serverProgressChanged |= advanced.Count != 0 || monsterAdvanced.Count != 0;
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

                case QuestCleared cleared:
                    var dependents = await AdvanceClearedQuestDependentsAsync(
                        log, cleared, cancellationToken).ConfigureAwait(false);
                    changes.AddRange(dependents);
                    serverProgressChanged |= dependents.Count != 0;
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
        && (target.Difficulty < 0 || target.Difficulty == (int)death.Difficulty)
        && target.EnemyCode == death.EnemyCode
        && target.EnemyType == death.EnemyType;

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

    private async ValueTask<IReadOnlyList<QuestProgressChange>> AdvanceMonsterQuestsAsync(
        IQuestLog log,
        QuestEnemyDefeated death,
        CancellationToken cancellationToken)
    {
        if (death.EnemyType != HuntEnemyTargetTypes.Monster)
            return [];

        var carried = await log.CarriedAsync(cancellationToken).ConfigureAwait(false);
        var changes = new List<QuestProgressChange>();
        foreach (var active in carried)
        {
            var definition = await definitions.FindAsync(active.QuestId, cancellationToken)
                .ConfigureAwait(false);
            if (definition?.HuntMonsterRequirements is not { Count: > 0 } targets)
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
                    $"Quest {active.QuestId} changed while reporting a verified monster death.");
            }

            changes.Add(new QuestProgressChange(active.QuestId, before.PackedValue,
                after.PackedValue));
        }

        return changes;
    }

    private static bool Matches(HuntMonsterRequirement target, QuestEnemyDefeated death)
    {
        if (death.EnemyCode <= 0
            || (target.DungeonId != -1 && target.DungeonId != death.DungeonId)
            || (int)death.Difficulty < target.MinimumDifficulty)
            return false;

        return target.MonsterCode switch
        {
            > 0 => target.MonsterCode == death.EnemyCode,
            -1 => death.Rank == MonsterRank.Ordinary,
            -2 => death.Rank == MonsterRank.Champion,
            -3 => death.Rank == MonsterRank.Boss,
            _ => false,
        };
    }

    private async ValueTask<IReadOnlyList<QuestProgressChange>> AdvanceClearedQuestDependentsAsync(
        IQuestLog log,
        QuestCleared cleared,
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
                || !requirements.Contains(cleared.QuestId))
                continue;

            var before = new QuestTrigger(unchecked((uint)active.TriggerValue));
            var missing = requirements.Count(id => !clearedIds.Contains(id));
            var after = before.ReplaceChannel(0, missing);
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
