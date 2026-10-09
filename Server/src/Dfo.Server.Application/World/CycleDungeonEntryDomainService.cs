using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class CycleDungeonEntryDomainService(ISelectedSessionDomainService sessions,
    IDungeonEntryIntentDomainService intents, ICharacterDomainService characters,
    ICharacterQuestLogDomainService quests, ICycleDungeonRuleService rules,
    IDungeonVisitDomainService visits) : ICycleDungeonEntryDomainService
{
    public async ValueTask<CycleDungeonPreparation> PrepareAsync(SelectedSessionIdentity session, int dungeonId, int questId,
        CancellationToken cancellationToken)
    {
        var before = intents.Find(session);
        if (sessions.Find(session.AccountId) != session || before is null
            || before.Value.Kind is not (DungeonEntryIntentKind.Interactive or DungeonEntryIntentKind.Cycle))
            return new(false, Refusal: "No current interactive selection.");
        var facts = await FactsAsync(session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        var region = facts is null ? null : await rules.EligibleAsync(dungeonId, questId, facts, cancellationToken).ConfigureAwait(false);
        if (region is null) return new(false, Refusal: "Cycle pair or character prerequisites rejected.");
        var carriedCycles = (await quests.LogOf(session.AccountId, session.CharacterId).CarriedAsync(cancellationToken)
            .ConfigureAwait(false)).Any(x => x.QuestId != questId && rules.RewardsForQuest(x.QuestId).Count > 0);
        if (carriedCycles) return new(false, Refusal: "Another cycle quest is still active.");
        if (sessions.Find(session.AccountId) != session
            || !intents.TryReplaceExact(session, before.Value, DungeonEntryIntent.Cycle(dungeonId, questId, region.Difficulty)))
            return new(false, Refusal: "Selection changed while preparing.");
        return new(true, questId);
    }

    public async ValueTask<bool> CanAcceptAsync(AccountId accountId, CharacterId characterId, int questId,
        CancellationToken cancellationToken)
    {
        var session = sessions.Find(accountId);
        if (session is null || session.CharacterId != characterId || intents.Find(session) is not { } intent
            || intent.Kind != DungeonEntryIntentKind.Cycle || intent.CycleQuestId != questId
            || intent.DirectedDungeonId is not > 0 || intent.CycleDifficulty is null) return false;
        var facts = await FactsAsync(accountId, characterId, cancellationToken).ConfigureAwait(false);
        var region = facts is null ? null : await rules.EligibleAsync(intent.DirectedDungeonId!.Value, questId, facts, cancellationToken).ConfigureAwait(false);
        return region?.Difficulty == intent.CycleDifficulty && sessions.Find(accountId) == session && intents.Find(session) == intent;
    }

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken cancellationToken)
    {
        var intent = context.Facts.Intent;
        if (intent.Kind != DungeonEntryIntentKind.Cycle || intent.DirectedDungeonId != context.DungeonId
            || intent.CycleQuestId is not > 0 || intent.CycleDifficulty is not { } difficulty
            || !Enum.IsDefined(difficulty)
            || context.RequestMode != 0 || context.PracticeMode != 0 || context.Participants.Count == 0)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite,
                $"Cycle selection mismatch: intent={intent}, requestedDungeon={context.DungeonId}, "
                + $"difficulty={context.Facts.Difficulty}, requestMode={context.RequestMode}, "
                + $"practiceMode={context.PracticeMode}, participants={context.Participants.Count}.");
        foreach (var member in context.Participants)
        {
            var facts = await FactsAsync(member.AccountId, member.CharacterId, cancellationToken).ConfigureAwait(false);
            var region = facts is null ? null : await rules.EligibleAsync(context.DungeonId, intent.CycleQuestId.Value, facts, cancellationToken).ConfigureAwait(false);
            if (region?.Difficulty != intent.CycleDifficulty || !facts!.CarriedQuestIds.Contains(intent.CycleQuestId.Value))
                return new(null, DungeonEntryFailure.MissingPrerequisite, member.Slot,
                    $"Cycle member prerequisites: eligible={region is not null}, "
                    + $"regionDifficulty={region?.Difficulty}, expectedDifficulty={intent.CycleDifficulty}, "
                    + $"questCarried={facts?.CarriedQuestIds.Contains(intent.CycleQuestId.Value) == true}.");
        }
        // The native cycle continuation may send the selection UI's difficulty (observed: 0).
        // Its authoritative difficulty comes from the prepared PVF region, checked above;
        // never let a generic SELECT_DUNGEON field override that fixed cycle rule.
        return await visits.TryEnterPartyAsync(context.Participants, context.DungeonId,
            context.Facts with { Difficulty = difficulty }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<WorldContentSubject?> FactsAsync(AccountId accountId, CharacterId characterId, CancellationToken ct)
    {
        var character = characters.Get(characterId);
        var identity = await character.IdentityAsync(ct).ConfigureAwait(false);
        var progress = await character.ProgressAsync(ct).ConfigureAwait(false);
        if (identity is null || progress is null) return null;
        var log = quests.LogOf(accountId, characterId);
        var cleared = await log.ClearedAsync(ct).ConfigureAwait(false);
        var carried = await log.CarriedAsync(ct).ConfigureAwait(false);
        return new(progress.Level, identity.Job, identity.GrowType,
            cleared.Where(x => x.Mark != 0).ToDictionary(x => x.QuestId, x => (int)x.Mark),
            carried.Select(x => x.QuestId).ToArray(), [], [], 0, 0);
    }
}
