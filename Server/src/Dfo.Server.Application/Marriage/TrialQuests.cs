using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>The pre-wedding quests: which relation each acceptance is bound to and the pair's verified trial clears.</summary>
internal sealed class TrialQuests(IMarriageQuestDomainStore bindings, IMarriageProposalRuleService rules, IQuestContentRuleService content,
    ICharacterQuestLogDomainService logs, ICharacterDomainService characters, IQuestAvailabilityRuleService availability,
    IMarriageDungeonContentSource dungeons, IEventService events, TimeProvider clock)
{
    public IReadOnlyCollection<int> QuestIds => [rules.TrialQuestId, rules.MaterialQuestId];

    public async ValueTask<bool> CanAcceptAsync(AccountId account, CharacterId character, int quest, Guid relation, CancellationToken ct)
    {
        if (!await RequirementsAsync(account, character, quest, ct).ConfigureAwait(false)) return false;
        var log = logs.LogOf(account, character);
        if ((await log.ClearedAsync(ct).ConfigureAwait(false)).Any(x => x.QuestId == quest && x.Mark != 0)) return false;
        return !(await log.CarriedAsync(ct).ConfigureAwait(false)).Any(x => x.QuestId == quest)
            || (await bindings.ReadAsync(character, quest, ct).ConfigureAwait(false))?.RelationId == relation;
    }

    public ValueTask BindAsync(CharacterId character, int quest, Guid relation, CancellationToken ct) =>
        bindings.BindAsync(character, quest, relation, clock.GetUtcNow(), ct);

    public async ValueTask<bool> CanCompleteAsync(AccountId account, CharacterId character, int quest, Guid relation, CancellationToken ct)
    {
        var bound = await bindings.ReadAsync(character, quest, ct).ConfigureAwait(false);
        if (bound?.RelationId != relation || !await RequirementsAsync(account, character, quest, ct).ConfigureAwait(false)) return false;
        return quest != rules.TrialQuestId || await bindings.CountClearsAsync(character, quest, relation, ct).ConfigureAwait(false)
            >= (await content.FindAsync(quest, ct).ConfigureAwait(false))!.WeddingTrialCount!.Value;
    }

    /// <summary>Both quests of every listed character are bound to the relation.</summary>
    public async ValueTask<bool> BoundToAsync(Guid relation, IEnumerable<CharacterId> characters, CancellationToken ct)
    {
        foreach (var character in characters)
            foreach (var quest in QuestIds)
                if ((await bindings.ReadAsync(character, quest, ct).ConfigureAwait(false))?.RelationId != relation) return false;
        return true;
    }

    /// <summary>A clear that can count as a trial for some pair: an ordinary boss room cleared by exactly two characters.</summary>
    public async ValueTask<bool> IsTrialClearAsync(DungeonMapCleared clear, CancellationToken ct) =>
        clear.IsBossRoom && clear.RunId.Value != Guid.Empty && clear.Participants.Length == 2
        && clear.Participants.Select(x => x.CharacterId).Distinct().Count() == 2
        && await dungeons.IsOrdinaryMapAsync(clear.DungeonId, clear.MapId, ct).ConfigureAwait(false);

    /// <summary>Counts the clear for each participant whose trial is carried, bound to the relation and accepted before the clear.</summary>
    public async ValueTask RecordClearAsync(DungeonMapCleared clear, Guid relation, CancellationToken ct)
    {
        foreach (var participant in clear.Participants)
        {
            var log = logs.LogOf(participant.AccountId, participant.CharacterId);
            var carried = (await log.CarriedAsync(ct).ConfigureAwait(false)).SingleOrDefault(x => x.QuestId == rules.TrialQuestId);
            var bound = await bindings.ReadAsync(participant.CharacterId, rules.TrialQuestId, ct).ConfigureAwait(false);
            if (carried is null || carried.TriggerValue <= 0 || bound?.RelationId != relation || bound.AcceptedAt > clear.ClearedAt) continue;
            if (!await bindings.RecordClearAsync(participant.CharacterId, rules.TrialQuestId, relation, clear.RunId, ct).ConfigureAwait(false)) continue;
            if (!await log.RecordProgressAsync(carried, carried.TriggerValue - 1, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Wedding trial changed while recording a verified clear.");
            await events.EnqueueAsync(new QuestProgressChanged(participant.CharacterId), ct).ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> RequirementsAsync(AccountId account, CharacterId character, int questId, CancellationToken ct)
    {
        if (questId != rules.TrialQuestId && questId != rules.MaterialQuestId) return false;
        var quest = await content.FindAsync(questId, ct).ConfigureAwait(false);
        if (quest?.Grade != QuestGrade.Wedding || questId == rules.TrialQuestId && quest.WeddingTrialCount is null) return false;
        var domain = characters.Get(character);
        var identity = await domain.IdentityAsync(ct).ConfigureAwait(false);
        var progress = await domain.ProgressAsync(ct).ConfigureAwait(false);
        if (identity is null || progress is null || progress.Level < rules.MinimumLevel) return false;
        var log = logs.LogOf(account, character);
        var cleared = await log.ClearedAsync(ct).ConfigureAwait(false);
        var carried = await log.CarriedAsync(ct).ConfigureAwait(false);
        var subject = new WorldContentSubject(progress.Level, identity.Job, identity.GrowType,
            cleared.Where(x => x.Mark != 0).ToDictionary(x => x.QuestId, x => (int)x.Mark),
            carried.Select(x => x.QuestId).ToArray(), [], [], 0, 0);
        return await availability.MeetsRequirementsAsync(quest, subject, ct).ConfigureAwait(false);
    }
}
