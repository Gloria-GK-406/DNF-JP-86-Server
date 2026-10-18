using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>The pre-wedding quests: which relation each acceptance is bound to and the pair's verified trial clears.</summary>
/// <remarks>
/// The quest services reach this through <see cref="IMarriageQuestDomainService"/>, so it reads relations and never takes the
/// marriage use cases: those reach the effect handlers, whose own dependencies lead back to the quest services.
/// </remarks>
internal sealed class MarriageQuestDomainService(IMarriageRelations relations, IMarriageQuestDomainStore bindings,
    IMarriageProposalRuleService rules, IQuestContentRuleService content, ICharacterQuestLogDomainService logs,
    ICharacterDomainService characters, IQuestAvailabilityRuleService availability, IMarriageTrialRuleService trials,
    IEventService events, TimeProvider clock) : IMarriageQuestDomainService
{
    private IReadOnlyCollection<int> QuestIds => [rules.TrialQuestId, rules.MaterialQuestId];

    /// <summary>The character's relation when the account owns that participant and the pair is engaged or further.</summary>
    private async ValueTask<MarriageRelation?> RelationAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        await relations.ForParticipantAsync(account, character, ct).ConfigureAwait(false) is { } relation
        && MarriageStageRules.IsCouple(relation.Stage) ? relation : null;

    public async ValueTask<IReadOnlyCollection<int>> EligibleQuestIdsAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        await RelationAsync(account, character, ct).ConfigureAwait(false) is null ? [] : QuestIds;

    public async ValueTask<bool> CanAcceptAsync(AccountId account, CharacterId character, int quest, CancellationToken ct)
    {
        if (await RelationAsync(account, character, ct).ConfigureAwait(false) is not { } relation
            || !await RequirementsAsync(account, character, quest, ct).ConfigureAwait(false)) return false;
        var log = logs.LogOf(account, character);
        if ((await log.ClearedAsync(ct).ConfigureAwait(false)).Any(x => x.QuestId == quest && x.Mark != 0)) return false;
        return !(await log.CarriedAsync(ct).ConfigureAwait(false)).Any(x => x.QuestId == quest)
            || (await bindings.ReadAsync(character, quest, ct).ConfigureAwait(false))?.RelationId == relation.Id;
    }

    public async ValueTask BindAsync(AccountId account, CharacterId character, int quest, CancellationToken ct)
    {
        var relation = await RelationAsync(account, character, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Wedding quest lost its engagement before acceptance.");
        await bindings.BindAsync(character, quest, relation.Id, clock.GetUtcNow(), ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> CanCompleteAsync(AccountId account, CharacterId character, int quest, CancellationToken ct)
    {
        if (await RelationAsync(account, character, ct).ConfigureAwait(false) is not { } relation) return false;
        var bound = await bindings.ReadAsync(character, quest, ct).ConfigureAwait(false);
        if (bound?.RelationId != relation.Id || !await RequirementsAsync(account, character, quest, ct).ConfigureAwait(false)) return false;
        return quest != rules.TrialQuestId || await bindings.CountClearsAsync(character, quest, relation.Id, ct).ConfigureAwait(false)
            >= (await content.FindAsync(quest, ct).ConfigureAwait(false))!.WeddingTrialCount!.Value;
    }

    /// <summary>Both have cleared the trial and material quests, and every one of those acceptances is bound to this relation.</summary>
    public async ValueTask<bool> ReadyForCeremonyAsync(Guid relation, SelectedSessionIdentity first, SelectedSessionIdentity second,
        CancellationToken ct)
    {
        foreach (var participant in new[] { first, second })
        {
            var cleared = await logs.LogOf(participant.AccountId, participant.CharacterId).ClearedAsync(ct).ConfigureAwait(false);
            foreach (var quest in QuestIds)
                if (!cleared.Any(x => x.QuestId == quest && x.Mark != 0)) return false;
        }
        foreach (var character in new[] { first.CharacterId, second.CharacterId })
            foreach (var quest in QuestIds)
                if ((await bindings.ReadAsync(character, quest, ct).ConfigureAwait(false))?.RelationId != relation) return false;
        return true;
    }

    /// <summary>Counts a verified pair clear towards both partners' trial when the two participants are exactly one relation.</summary>
    public async ValueTask RecordTrialClearAsync(DungeonMapCleared clear, CancellationToken ct)
    {
        if (!clear.IsBossRoom || clear.RunId.Value == Guid.Empty || clear.Participants.Length != 2
            || clear.Participants.Select(x => x.CharacterId).Distinct().Count() != 2
            || !await trials.IsTrialMapAsync(clear.DungeonId, clear.MapId, ct).ConfigureAwait(false)) return;
        var relation = await RelationAsync(clear.Participants[0].AccountId, clear.Participants[0].CharacterId, ct).ConfigureAwait(false);
        if (relation is null || !clear.Participants.Select(x => x.CharacterId).ToHashSet()
            .SetEquals([relation.Proposer.CharacterId, relation.Recipient.CharacterId])) return;
        if (clear.Participants.Any(x => !IMarriageRelations.Belongs(relation, x.AccountId, x.CharacterId))) return;
        foreach (var participant in clear.Participants)
        {
            var log = logs.LogOf(participant.AccountId, participant.CharacterId);
            var carried = (await log.CarriedAsync(ct).ConfigureAwait(false)).SingleOrDefault(x => x.QuestId == rules.TrialQuestId);
            var bound = await bindings.ReadAsync(participant.CharacterId, rules.TrialQuestId, ct).ConfigureAwait(false);
            if (carried is null || carried.TriggerValue <= 0 || bound?.RelationId != relation.Id || bound.AcceptedAt > clear.ClearedAt) continue;
            if (!await bindings.RecordClearAsync(participant.CharacterId, rules.TrialQuestId, relation.Id, clear.RunId, ct).ConfigureAwait(false)) continue;
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
