using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed class MarriageCeremonyDomainService(
    IMarriageCeremonyDomainStore store,
    IMarriageProposalDomainStore relations,
    IMarriageCeremonyRuleService rules,
    IMarriageQuestDomainStore bindings,
    ICharacterQuestLogDomainService quests,
    ICharacterDomainService characters,
    ISelectedSessionDomainService sessions,
    ITownAreaDomainService areas,
    TimeProvider clock, IMarriageWeddingDomainStore weddings) : IMarriageCeremonyDomainService
{
    public ValueTask<MarriageCeremony?> FindReceivedAsync(CharacterId character, CancellationToken ct) =>
        store.FindLatestReceivedAsync(character, ct);

    public async ValueTask<MarriageCeremonyResult> RequestAsync(SelectedSessionIdentity applicant,
        SelectedSessionIdentity partner, int tier, int waitIndex, CancellationToken ct)
    {
        var requiredGold = rules.RequiredGold(tier, waitIndex);
        if (requiredGold is null) return new(MarriageCeremonyFailure.Invalid);
        if (!Current(applicant) || !Current(partner)) return new(MarriageCeremonyFailure.Stale);
        if (applicant.AccountId == partner.AccountId || applicant.CharacterId == partner.CharacterId)
            return new(MarriageCeremonyFailure.Invalid);
        if (!SameTown(applicant, partner)) return new(MarriageCeremonyFailure.Unavailable);
        var relation = await RelationAsync(applicant, partner, ct).ConfigureAwait(false);
        if (relation is null || await weddings.IsMarriedAsync(relation.Id, ct).ConfigureAwait(false)) return new(MarriageCeremonyFailure.Ineligible);
        var existing = await ActiveAsync(relation.Id, ct).ConfigureAwait(false);
        if (existing is not null)
            return existing.State == MarriageCeremonyState.Pending
                && Matches(existing, applicant, partner, tier, waitIndex)
                ? new(MarriageCeremonyFailure.None, existing)
                : new(MarriageCeremonyFailure.Occupied);
        if (!await EligibleAsync(relation.Id, applicant, partner, ct).ConfigureAwait(false))
            return new(MarriageCeremonyFailure.Ineligible);
        if (await characters.Get(applicant.CharacterId).GoldAsync(ct).ConfigureAwait(false) < requiredGold.Value)
            return new(MarriageCeremonyFailure.InsufficientFunds);
        var now = clock.GetUtcNow();
        var ceremony = new MarriageCeremony(Guid.NewGuid(), relation.Id, applicant, partner,
            tier, waitIndex, MarriageCeremonyState.Pending, 1, now, now + rules.Lifetime);
        await store.CreateAsync(ceremony, ct).ConfigureAwait(false);
        return new(MarriageCeremonyFailure.None, ceremony, true);
    }

    public async ValueTask<MarriageCeremonyResult> RespondAsync(SelectedSessionIdentity partner,
        SelectedSessionIdentity applicant, byte response, CancellationToken ct)
    {
        if (response > 2) return new(MarriageCeremonyFailure.Invalid);
        var ceremony = await store.FindLatestReceivedAsync(partner.CharacterId, ct).ConfigureAwait(false);
        if (ceremony is null || !Matches(ceremony, applicant, partner, ceremony.Tier, ceremony.WaitIndex)
            || !Current(applicant) || !Current(partner)) return new(MarriageCeremonyFailure.Stale);
        if (ceremony.State != MarriageCeremonyState.Pending)
            return ceremony.Response == response ? new(MarriageCeremonyFailure.None, ceremony)
                : new(MarriageCeremonyFailure.Stale);
        if (ceremony.ExpiresAt <= clock.GetUtcNow())
        {
            var changed = await store.TransitionAsync(ceremony, MarriageCeremonyState.Expired, 2, ct).ConfigureAwait(false);
            var expired = ceremony with { State = MarriageCeremonyState.Expired,
                Revision = ceremony.Revision + (changed ? 1 : 0), Response = 2 };
            return response == 2 && changed ? new(MarriageCeremonyFailure.None, expired, true)
                : new(MarriageCeremonyFailure.Stale, expired, changed);
        }
        if (response == 0)
        {
            if (!SameTown(applicant, partner))
                return await FailAndExpireAsync(ceremony, MarriageCeremonyFailure.Unavailable, ct).ConfigureAwait(false);
            var relation = await RelationAsync(applicant, partner, ct).ConfigureAwait(false);
            if (relation?.Id != ceremony.RelationId
                || !await EligibleAsync(ceremony.RelationId, applicant, partner, ct).ConfigureAwait(false))
                return await FailAndExpireAsync(ceremony, MarriageCeremonyFailure.Ineligible, ct).ConfigureAwait(false);
            var requiredGold = rules.RequiredGold(ceremony.Tier, ceremony.WaitIndex);
            if (requiredGold is null)
                return await FailAndExpireAsync(ceremony, MarriageCeremonyFailure.Invalid, ct).ConfigureAwait(false);
            if (await characters.Get(applicant.CharacterId).GoldAsync(ct).ConfigureAwait(false) < requiredGold.Value)
                return await FailAndExpireAsync(ceremony, MarriageCeremonyFailure.InsufficientFunds, ct).ConfigureAwait(false);
        }
        var state = response switch
        {
            0 => MarriageCeremonyState.Reserved,
            1 => MarriageCeremonyState.Rejected,
            _ => MarriageCeremonyState.Expired,
        };
        if (!await store.TransitionAsync(ceremony, state, response, ct).ConfigureAwait(false))
            return new(MarriageCeremonyFailure.Stale);
        return new(MarriageCeremonyFailure.None, ceremony with
        {
            State = state,
            Revision = ceremony.Revision + 1,
            Response = response,
        }, true);
    }

    private async ValueTask<MarriageCeremonyResult> FailAndExpireAsync(MarriageCeremony ceremony,
        MarriageCeremonyFailure failure, CancellationToken ct)
    {
        if (!await store.TransitionAsync(ceremony, MarriageCeremonyState.Expired, 2, ct).ConfigureAwait(false))
            return new(MarriageCeremonyFailure.Stale);
        return new(failure, ceremony with
        {
            State = MarriageCeremonyState.Expired,
            Revision = ceremony.Revision + 1,
            Response = 2,
        }, true);
    }

    private async ValueTask<MarriageProposal?> RelationAsync(SelectedSessionIdentity first,
        SelectedSessionIdentity second, CancellationToken ct)
    {
        var relation = await relations.FindActiveAsync(first.CharacterId, ct).ConfigureAwait(false);
        var peer = await relations.FindActiveAsync(second.CharacterId, ct).ConfigureAwait(false);
        if (relation is not { State: MarriageProposalState.Engaged } || peer?.Id != relation.Id) return null;
        var direct = relation.Proposer.AccountId == first.AccountId && relation.Proposer.CharacterId == first.CharacterId
            && relation.Recipient.AccountId == second.AccountId && relation.Recipient.CharacterId == second.CharacterId;
        var reverse = relation.Recipient.AccountId == first.AccountId && relation.Recipient.CharacterId == first.CharacterId
            && relation.Proposer.AccountId == second.AccountId && relation.Proposer.CharacterId == second.CharacterId;
        return direct || reverse ? relation : null;
    }

    private async ValueTask<MarriageCeremony?> ActiveAsync(Guid relationId, CancellationToken ct)
    {
        var active = await store.FindActiveByRelationAsync(relationId, ct).ConfigureAwait(false);
        if (active is not { State: MarriageCeremonyState.Pending }) return active;
        if (active.ExpiresAt > clock.GetUtcNow() && Current(active.Applicant) && Current(active.Partner))
            return active;
        if (!await store.TransitionAsync(active, MarriageCeremonyState.Expired, 2, ct).ConfigureAwait(false))
            return await store.FindActiveByRelationAsync(relationId, ct).ConfigureAwait(false);
        return null;
    }

    private async ValueTask<bool> EligibleAsync(Guid relationId, SelectedSessionIdentity first,
        SelectedSessionIdentity second, CancellationToken ct)
    {
        foreach (var participant in new[] { first, second })
        {
            var cleared = await quests.LogOf(participant.AccountId, participant.CharacterId)
                .ClearedAsync(ct).ConfigureAwait(false);
            foreach (var quest in new[] { rules.TrialQuestId, rules.MaterialQuestId })
            {
                if (!cleared.Any(x => x.QuestId == quest && x.Mark != 0)) return false;
                if ((await bindings.ReadAsync(participant.CharacterId, quest, ct).ConfigureAwait(false))?.RelationId != relationId)
                    return false;
            }
        }
        return true;
    }

    private static bool Matches(MarriageCeremony ceremony, SelectedSessionIdentity applicant,
        SelectedSessionIdentity partner, int tier, int waitIndex) =>
        ceremony.Applicant == applicant
        && ceremony.Partner == partner
        && ceremony.Tier == tier && ceremony.WaitIndex == waitIndex;

    private bool Current(SelectedSessionIdentity session) => sessions.Find(session.AccountId) == session;
    private bool SameTown(SelectedSessionIdentity first, SelectedSessionIdentity second) =>
        first.ListenerPort == second.ListenerPort && areas.Find(first.CharacterId) is { SharesTownPresence: true } a
        && areas.Find(second.CharacterId) is { SharesTownPresence: true } b && a.Area == b.Area
        && a.Lease == first.TownAreaLease() && b.Lease == second.TownAreaLease();
}
