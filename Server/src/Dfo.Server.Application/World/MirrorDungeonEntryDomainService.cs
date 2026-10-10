using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class MirrorDungeonEntryDomainService(IMirrorDungeonRuleService rules,
    IDungeonStructureRuleService structures, IDungeonVisitDomainService visits,
    ISelectedSessionDomainService sessions, ICharacterDomainService characters,
    ICharacterPositionDomainService positions, ICharacterQuestLogDomainService quests,
    IDungeonRunDomainService runs, IMirrorDungeonQuotaDomainService quota,
    IDomainTransactionAccessor transactions) : IMirrorDungeonEntryDomainService
{
    private readonly ConcurrentDictionary<Dfo.Server.Data.Characters.CharacterId, int> testRotation = new();

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (transactions.Current is null) throw new InvalidOperationException("Mirror entry requires the shared entry transaction.");
        if (context.MirrorQuestId is not > 0 || context.RequestMode != 0 || context.PracticeMode != 0
            || context.Participants.Count is < 1 or > 4
            || context.Participants.Count(x => x.IsLeader) != 1
            || context.Participants.Select(x => x.CharacterId).Distinct().Count() != context.Participants.Count
            || context.Participants.Select(x => x.AccountId).Distinct().Count() != context.Participants.Count
            || context.Participants.Select(x => x.Slot).Distinct().Count() != context.Participants.Count)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);

        MirrorDungeonEntryRule? rule;
        try
        {
            rule = await rules.ResolveAsync(context.DungeonId, context.MirrorQuestId.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException error)
        {
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message);
        }
        if (rule is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite, "Invalid mirror dungeon/quest pair.");
        var catalog = rule.Catalog;
        var intent = context.Facts.Intent;
        if (intent.Kind != DungeonEntryIntentKind.Interactive
            && (intent.Kind != DungeonEntryIntentKind.Directed || intent.DirectedDungeonId != catalog.WrapperDungeonId))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite, "Mirror selection context is not current.");

        var selected = new List<Dfo.Server.Data.World.SelectedSessionIdentity>();
        foreach (var member in context.Participants)
        {
            var session = sessions.Find(member.AccountId);
            if (session is null || session.CharacterId != member.CharacterId)
                return new(null, DungeonEntryFailure.MissingPrerequisite, member.Slot);
            selected.Add(session);
            if (runs.Find(member.CharacterId) is not null)
                return new(null, DungeonEntryFailure.AlreadyInDungeon, member.Slot);
            var position = positions.Find(member.CharacterId);
            if (position is null || position.TownId != catalog.EntryTownId || position.AreaId != catalog.EntryAreaId)
                return new(null, DungeonEntryFailure.MissingPrerequisite, member.Slot);
            var progress = await characters.Get(member.CharacterId).ProgressAsync(cancellationToken).ConfigureAwait(false);
            if (progress is null || progress.Level < catalog.MinimumLevel)
                return new(null, DungeonEntryFailure.LevelTooLow, member.Slot);
            var cleared = await quests.LogOf(member.AccountId, member.CharacterId).ClearedAsync(cancellationToken).ConfigureAwait(false);
            if (catalog.RequiredQuestIds.Any(id => !cleared.Any(x => x.QuestId == id && x.Mark != 0)))
                return new(null, DungeonEntryFailure.MissingPrerequisite, member.Slot);
        }
        var test = rules.EntryTestSettings;
        var leader = context.Participants.Single(x => x.IsLeader).CharacterId;
        var cursor = 0;
        var dungeonId = context.DungeonId;
        var questId = context.MirrorQuestId.Value;
        if (test.Enabled)
        {
            if (test.DungeonIds.Count == 0)
                return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, "Mirror test rotation is empty.");
            cursor = testRotation.GetOrAdd(leader, 0);
            dungeonId = test.DungeonIds[cursor];
            MirrorDungeonEntryRule? testRule = null;
            try
            {
                foreach (var candidate in catalog.Candidates.Where(x => x.DungeonId == dungeonId))
                {
                    testRule = await rules.ResolveAsync(dungeonId, candidate.QuestId, cancellationToken).ConfigureAwait(false);
                    if (testRule is null) continue;
                    questId = candidate.QuestId;
                    break;
                }
            }
            catch (InvalidDataException error)
            {
                return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message);
            }
            if (testRule is null)
                return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, $"Mirror test dungeon {dungeonId} has no valid mission pair.");
            rule = testRule;
        }
        var skipQuota = test.Enabled && test.SkipEntryQuota;
        if (!skipQuota && await quota.FindBlockedSlotAsync(context.Participants, catalog.DailyEntryLimit, cancellationToken).ConfigureAwait(false) is { } blocked)
            return new(null, DungeonEntryFailure.DailyEntryLimitReached, blocked);

        // Mirror enters the historical dungeon's ordinary maze, independently of the
        // character's story-maze bindings and historical-map difficulty progression.
        var facts = context.Facts with { Difficulty = rule.Difficulty, Intent = DungeonEntryIntent.Interactive };
        DungeonLayout? layout;
        try
        {
            layout = await structures.ResolveAsync(dungeonId, facts, Random.Shared.Next(), cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException error)
        {
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message);
        }
        if (layout is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, "Mirror historical maze unavailable.");
        layout = layout with { Mirror = new(catalog.WrapperDungeonId, catalog.WorldMapId, questId) };
        if (selected.Any(x => sessions.Find(x.AccountId) != x))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite, "Mirror participants changed during entry.");
        var attempt = await visits.TryEnterPreparedPartyAsync(context.Participants, layout, facts, cancellationToken).ConfigureAwait(false);
        if (attempt.Entry is null) return attempt;
        if (!skipQuota && await quota.TryReserveEntryAsync(context.Participants, catalog.DailyEntryLimit, cancellationToken).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("Validated mirror entry quota changed in the shared transaction.");
        if (test.Enabled)
            transactions.Current.RegisterAfterCommit((this, leader), _ =>
            {
                testRotation[leader] = (cursor + 1) % test.DungeonIds.Count;
                return ValueTask.CompletedTask;
            });
        return attempt;
    }
}
