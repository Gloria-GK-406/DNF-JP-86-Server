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

internal sealed class MarriageQuestDomainService(IMarriageProposalDomainStore relations,
    IMarriageQuestDomainStore bindings, IMarriageProposalRuleService rules, IQuestContentRuleService content,
    ICharacterQuestLogDomainService logs, ICharacterDomainService characters,
    IQuestAvailabilityRuleService availability, IMarriageDungeonContentSource dungeons, IEventService events,
    TimeProvider clock) : IMarriageQuestDomainService, IDomainEventSubscriber<DungeonMapCleared>
{
    private async ValueTask<MarriageProposal?> RelationAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        var relation = await relations.FindActiveAsync(character, ct).ConfigureAwait(false);
        if (relation is not { State: MarriageProposalState.Engaged }) return null;
        return relation.Proposer.AccountId == account && relation.Proposer.CharacterId == character
            || relation.Recipient.AccountId == account && relation.Recipient.CharacterId == character ? relation : null;
    }

    public async ValueTask<IReadOnlyCollection<int>> EligibleQuestIdsAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        await RelationAsync(account, character, ct).ConfigureAwait(false) is null ? [] : [rules.TrialQuestId, rules.MaterialQuestId];

    public async ValueTask<bool> CanAcceptAsync(AccountId account, CharacterId character, int quest, CancellationToken ct)
    {
        var relation = await RelationAsync(account, character, ct).ConfigureAwait(false);
        if (relation is null) return false;
        if (!await RequirementsAsync(account, character, quest, ct).ConfigureAwait(false)) return false;
        var log = logs.LogOf(account, character);
        if ((await log.ClearedAsync(ct).ConfigureAwait(false)).Any(x => x.QuestId == quest && x.Mark != 0)) return false;
        return !(await log.CarriedAsync(ct).ConfigureAwait(false)).Any(x => x.QuestId == quest)
            || (await bindings.ReadAsync(character, quest, ct).ConfigureAwait(false))?.RelationId == relation.Id;
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

    public async ValueTask BindAsync(AccountId account, CharacterId character, int quest, CancellationToken ct)
    {
        var relation = await RelationAsync(account, character, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Wedding quest lost its engagement before acceptance.");
        await bindings.BindAsync(character, quest, relation.Id, clock.GetUtcNow(), ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> CanCompleteAsync(AccountId account, CharacterId character, int quest, CancellationToken ct)
    {
        var relation = await RelationAsync(account, character, ct).ConfigureAwait(false);
        var bound = await bindings.ReadAsync(character, quest, ct).ConfigureAwait(false);
        if (relation is null || bound?.RelationId != relation.Id
            || !await RequirementsAsync(account, character, quest, ct).ConfigureAwait(false)) return false;
        return quest != rules.TrialQuestId || await bindings.CountClearsAsync(character, quest, relation.Id, ct).ConfigureAwait(false)
            >= (await content.FindAsync(quest, ct).ConfigureAwait(false))!.WeddingTrialCount!.Value;
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<DungeonMapCleared> message, CancellationToken ct)
    {
        var clear = message.Payload;
        if (!clear.IsBossRoom || clear.RunId.Value == Guid.Empty || clear.Participants.Length != 2
            || clear.Participants.Select(x => x.CharacterId).Distinct().Count() != 2
            || !await dungeons.IsOrdinaryMapAsync(clear.DungeonId, clear.MapId, ct).ConfigureAwait(false)) return;
        var relation = await RelationAsync(clear.Participants[0].AccountId, clear.Participants[0].CharacterId, ct).ConfigureAwait(false);
        if (relation is null || !clear.Participants.Select(x => x.CharacterId).ToHashSet()
            .SetEquals([relation.Proposer.CharacterId, relation.Recipient.CharacterId])) return;
        if (clear.Participants.Any(x => !((x.AccountId == relation.Proposer.AccountId && x.CharacterId == relation.Proposer.CharacterId)
            || (x.AccountId == relation.Recipient.AccountId && x.CharacterId == relation.Recipient.CharacterId)))) return;
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
}
