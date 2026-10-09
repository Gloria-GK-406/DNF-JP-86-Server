using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

internal sealed class CycleDungeonEntryDomainService(ISelectedSessionDomainService sessions,
    IDungeonEntryIntentDomainService intents, ICharacterDomainService characters,
    ICharacterQuestLogDomainService quests, ICycleDungeonRuleService rules,
    IDungeonVisitDomainService visits, ICharacterDungeonDifficultyDomainService difficulties,
    IDungeonDifficultyRuleService difficultyRules, ILogger<CycleDungeonEntryDomainService> logger) : ICycleDungeonEntryDomainService
{
    public async ValueTask<CycleDungeonPreparation> PrepareAsync(SelectedSessionIdentity session, int dungeonId, int questId,
        CancellationToken cancellationToken)
    {
        var before = intents.Find(session);
        if (sessions.Find(session.AccountId) != session || before is null)
            return new(false, Refusal: "No current dungeon selection.");
        var facts = await FactsAsync(session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        var region = facts is null ? null : await rules.EligibleAsync(dungeonId, questId, facts, cancellationToken).ConfigureAwait(false);
        if (region is null) return new(false, Refusal: "Cycle pair or character prerequisites rejected.");
        // A21 retry opens selection with the previous dungeon ID (000F), then prepares
        // a new random pair (0308). That nonzero selection is initially Directed, but
        // this explicit cycle command may rebind it within the eligible region's pool.
        if (before.Value.Kind == DungeonEntryIntentKind.Directed
            && !region.Candidates.Any(x => x.DungeonId == before.Value.DirectedDungeonId))
            return new(false, Refusal: "Directed selection is outside the eligible cycle region.");
        if (before.Value.Kind is not (DungeonEntryIntentKind.Interactive or DungeonEntryIntentKind.Cycle or DungeonEntryIntentKind.Directed))
            return new(false, Refusal: "Selection does not support cycle preparation.");
        // The native retry sends preparation before abandoning its previous quest.
        // Preparation changes only the selection; persisted quest conflicts belong
        // to acceptance, after the client has performed its own cancellation.
        if (sessions.Find(session.AccountId) != session
            || !intents.TryReplaceExact(session, before.Value, DungeonEntryIntent.Cycle(dungeonId, questId)))
            return new(false, Refusal: "Selection changed while preparing.");
        return new(true, questId);
    }

    public async ValueTask<bool> CanAcceptAsync(AccountId accountId, CharacterId characterId, int questId,
        CancellationToken cancellationToken)
    {
        var session = sessions.Find(accountId);
        if (session is null || session.CharacterId != characterId || intents.Find(session) is not { } intent
            || intent.Kind != DungeonEntryIntentKind.Cycle || intent.CycleQuestId != questId
            || intent.DirectedDungeonId is not > 0) return false;
        var facts = await FactsAsync(accountId, characterId, cancellationToken).ConfigureAwait(false);
        var region = facts is null ? null : await rules.EligibleAsync(intent.DirectedDungeonId!.Value, questId, facts, cancellationToken).ConfigureAwait(false);
        if (region is null) return false;
        var carried = await quests.LogOf(accountId, characterId).CarriedAsync(cancellationToken).ConfigureAwait(false);
        if (carried.Any(x => x.QuestId != questId && rules.RewardsForQuest(x.QuestId).Count > 0)) return false;
        return sessions.Find(accountId) == session && intents.Find(session) == intent;
    }

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken cancellationToken)
    {
        var intent = context.Facts.Intent;
        if (intent.Kind != DungeonEntryIntentKind.Cycle || intent.DirectedDungeonId != context.DungeonId
            || intent.CycleQuestId is not > 0
            || context.RequestMode != 0 || context.PracticeMode != 0 || context.Participants.Count == 0)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite,
                $"Cycle selection mismatch: intent={intent}, requestedDungeon={context.DungeonId}, "
                + $"difficulty={context.Facts.Difficulty}, requestMode={context.RequestMode}, "
                + $"practiceMode={context.PracticeMode}, participants={context.Participants.Count}.");
        if (!Enum.IsDefined(context.Facts.Difficulty))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.DifficultyNotUnlocked);
        var commonMaximum = DungeonDifficulty.Tier4;
        foreach (var member in context.Participants)
        {
            var facts = await FactsAsync(member.AccountId, member.CharacterId, cancellationToken).ConfigureAwait(false);
            var region = facts is null ? null : await rules.EligibleAsync(context.DungeonId, intent.CycleQuestId.Value, facts, cancellationToken).ConfigureAwait(false);
            if (region is null || !facts!.CarriedQuestIds.Contains(intent.CycleQuestId.Value))
                return new(null, DungeonEntryFailure.MissingPrerequisite, member.Slot,
                    $"Cycle member prerequisites: eligible={region is not null}, "
                    + $"questCarried={facts?.CarriedQuestIds.Contains(intent.CycleQuestId.Value) == true}.");
            var maximum = await difficulties.GetMaximumAsync(member.CharacterId, context.DungeonId, cancellationToken).ConfigureAwait(false);
            if (!Enum.IsDefined(maximum)) return new(null, DungeonEntryFailure.DifficultyNotUnlocked, member.Slot);
            if (maximum < commonMaximum) commonMaximum = maximum;
        }
        DungeonDifficulty? difficulty;
        try
        {
            difficulty = await difficultyRules.NearestUnlockedAsync(context.DungeonId,
                context.Facts.Difficulty, commonMaximum, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException error)
        {
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message);
        }
        if (difficulty is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DifficultyNotUnlocked);
        logger.LogInformation("Cycle difficulty resolved: dungeon={DungeonId} quest={QuestId} requested={Requested} actual={Actual} commonMaximum={Maximum}",
            context.DungeonId, intent.CycleQuestId, context.Facts.Difficulty, difficulty.Value, commonMaximum);
        // Resolve against this map's supported/unlocked tiers before choosing a maze or paying.
        // The resulting run and its entry notifications carry this actual difficulty.
        return await visits.TryEnterPartyAsync(context.Participants, context.DungeonId,
            context.Facts with { Difficulty = difficulty.Value }, cancellationToken).ConfigureAwait(false);
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
