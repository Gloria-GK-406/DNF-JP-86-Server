using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed partial class MarriageDomainService
{
    public async ValueTask<MarriageProposalResult> ProposeAsync(SelectedSessionIdentity proposer,
        SelectedSessionIdentity recipient, CancellationToken ct)
    {
        if (!eligibility.IsCurrent(proposer) || !eligibility.IsCurrent(recipient)) return new(MarriageProposalFailure.Stale);
        if (proposer.AccountId == recipient.AccountId || proposer.CharacterId == recipient.CharacterId)
            return new(MarriageProposalFailure.Invalid);
        if (!eligibility.SameTown(proposer, recipient)) return new(MarriageProposalFailure.Unavailable);
        if (!await eligibility.CanPairAsync(proposer, recipient, ct).ConfigureAwait(false)) return new(MarriageProposalFailure.Ineligible);
        var existing = await ActiveProposalAsync(proposer.CharacterId, ct).ConfigureAwait(false);
        var other = await ActiveProposalAsync(recipient.CharacterId, ct).ConfigureAwait(false);
        if (existing is { State: MarriageProposalState.Pending } && existing.Proposer == proposer
            && existing.Recipient == recipient && other?.Id == existing.Id) return new(MarriageProposalFailure.None, existing);
        if (existing is not null || other is not null) return new(MarriageProposalFailure.Occupied);
        var proposal = await ledger.ProposeAsync(proposer, recipient, ct).ConfigureAwait(false);
        return new(MarriageProposalFailure.None, proposal, true);
    }

    public async ValueTask<MarriageProposalResult> RespondProposalAsync(SelectedSessionIdentity recipient,
        SelectedSessionIdentity proposer, byte response, CancellationToken ct)
    {
        if (response > 2) return new(MarriageProposalFailure.Invalid);
        var proposal = await ledger.ReceivedProposalAsync(recipient.CharacterId, ct).ConfigureAwait(false);
        if (proposal is null || proposal.Proposer != proposer || proposal.Recipient != recipient
            || !eligibility.IsCurrent(recipient) || !eligibility.IsCurrent(proposer)) return new(MarriageProposalFailure.Stale);
        if (proposal.State != MarriageProposalState.Pending)
            return proposal.Response == response ? new(MarriageProposalFailure.None, proposal)
                : new(MarriageProposalFailure.Stale);
        var relation = await ledger.ReadAsync(proposal.Id, ct).ConfigureAwait(false);
        if (relation is not { Stage: MarriageStage.Proposed }) return new(MarriageProposalFailure.Stale);
        if (proposal.ExpiresAt <= clock.GetUtcNow())
        {
            var changed = await ledger.TransitionProposalAsync(proposal, MarriageProposalState.Expired, 2, ct).ConfigureAwait(false);
            if (changed) await ledger.MustCommitAsync(relation, MarriageTrigger.ProposalLapsed, null, null, ct).ConfigureAwait(false);
            return response == 2 && changed ? new(MarriageProposalFailure.None,
                proposal with { State = MarriageProposalState.Expired, Revision = proposal.Revision + 1, Response = 2 }, true)
                : new(MarriageProposalFailure.Stale);
        }
        if (response == 0 && !eligibility.SameTown(proposer, recipient)) return new(MarriageProposalFailure.Unavailable);
        if (response == 0 && !await eligibility.CanPairAsync(proposer, recipient, ct).ConfigureAwait(false)) return new(MarriageProposalFailure.Ineligible);
        var state = response switch { 0 => MarriageProposalState.Engaged, 1 => MarriageProposalState.Rejected, _ => MarriageProposalState.Expired };
        if (!await ledger.TransitionProposalAsync(proposal, state, response, ct).ConfigureAwait(false))
            return new(MarriageProposalFailure.Stale);
        var trigger = response switch { 0 => MarriageTrigger.AcceptProposal, 1 => MarriageTrigger.RefuseProposal, _ => MarriageTrigger.ProposalLapsed };
        await ledger.MustCommitAsync(relation, trigger, null, null, ct).ConfigureAwait(false);
        return new(MarriageProposalFailure.None, proposal with { State = state, Revision = proposal.Revision + 1, Response = response }, true);
    }

    public async ValueTask<MarriageCeremonyResult> RequestReservationAsync(SelectedSessionIdentity applicant,
        SelectedSessionIdentity partner, int tier, int waitIndex, CancellationToken ct)
    {
        var requiredGold = reservations.RequiredGold(tier, waitIndex);
        if (requiredGold is null) return new(MarriageCeremonyFailure.Invalid);
        if (!eligibility.IsCurrent(applicant) || !eligibility.IsCurrent(partner)) return new(MarriageCeremonyFailure.Stale);
        if (applicant.AccountId == partner.AccountId || applicant.CharacterId == partner.CharacterId)
            return new(MarriageCeremonyFailure.Invalid);
        if (!eligibility.SameTown(applicant, partner)) return new(MarriageCeremonyFailure.Unavailable);
        var relation = await ledger.ForPairAsync(applicant, partner, ct).ConfigureAwait(false);
        if (relation is null || !MarriageStageRules.IsCouple(relation.Stage) || relation.Stage == MarriageStage.Married)
            return new(MarriageCeremonyFailure.Ineligible);
        (var existing, relation) = await ActiveReservationAsync(relation, ct).ConfigureAwait(false);
        if (existing is not null)
            return existing.State == MarriageCeremonyState.Pending
                && ReservationDesk.Matches(existing, applicant, partner, tier, waitIndex)
                ? new(MarriageCeremonyFailure.None, existing)
                : new(MarriageCeremonyFailure.Occupied);
        if (!await WeddingQuestsReadyAsync(relation.Id, applicant, partner, ct).ConfigureAwait(false))
            return new(MarriageCeremonyFailure.Ineligible);
        if (!await eligibility.CanAffordAsync(applicant.CharacterId, requiredGold.Value, ct).ConfigureAwait(false))
            return new(MarriageCeremonyFailure.InsufficientFunds);
        if (MarriageStageRules.Next(relation.Stage, MarriageTrigger.RequestCeremony) is null) return new(MarriageCeremonyFailure.Stale);
        var ceremony = await reservations.CreateAsync(relation.Id, applicant, partner, tier, waitIndex, ct).ConfigureAwait(false);
        await ledger.MustCommitAsync(relation, MarriageTrigger.RequestCeremony, ceremony.Id, null, ct).ConfigureAwait(false);
        return new(MarriageCeremonyFailure.None, ceremony, true);
    }

    public async ValueTask<MarriageCeremonyResult> RespondReservationAsync(SelectedSessionIdentity partner,
        SelectedSessionIdentity applicant, byte response, CancellationToken ct)
    {
        if (response > 2) return new(MarriageCeremonyFailure.Invalid);
        var ceremony = await reservations.ReceivedAsync(partner.CharacterId, ct).ConfigureAwait(false);
        if (ceremony is null || !ReservationDesk.Matches(ceremony, applicant, partner, ceremony.Tier, ceremony.WaitIndex)
            || !eligibility.IsCurrent(applicant) || !eligibility.IsCurrent(partner)) return new(MarriageCeremonyFailure.Stale);
        if (ceremony.State != MarriageCeremonyState.Pending)
            return ceremony.Response == response ? new(MarriageCeremonyFailure.None, ceremony)
                : new(MarriageCeremonyFailure.Stale);
        var relation = await ledger.ReadAsync(ceremony.RelationId, ct).ConfigureAwait(false);
        if (relation is not { Stage: MarriageStage.Requested } || relation.CeremonyId != ceremony.Id)
            return new(MarriageCeremonyFailure.Stale);
        if (ceremony.ExpiresAt <= clock.GetUtcNow())
        {
            var changed = await reservations.TransitionAsync(ceremony, MarriageCeremonyState.Expired, 2, ct).ConfigureAwait(false);
            if (changed) await ledger.MustCommitAsync(relation, MarriageTrigger.CeremonyWithdrawn, null, null, ct).ConfigureAwait(false);
            var expired = ceremony with { State = MarriageCeremonyState.Expired,
                Revision = ceremony.Revision + (changed ? 1 : 0), Response = 2 };
            return response == 2 && changed ? new(MarriageCeremonyFailure.None, expired, true)
                : new(MarriageCeremonyFailure.Stale, expired, changed);
        }
        if (response == 0)
        {
            if (!eligibility.SameTown(applicant, partner))
                return await FailAndExpireAsync(ceremony, relation, MarriageCeremonyFailure.Unavailable, ct).ConfigureAwait(false);
            if (!CoupleLedger.IsPair(relation, applicant, partner)
                || !await WeddingQuestsReadyAsync(ceremony.RelationId, applicant, partner, ct).ConfigureAwait(false))
                return await FailAndExpireAsync(ceremony, relation, MarriageCeremonyFailure.Ineligible, ct).ConfigureAwait(false);
            var requiredGold = reservations.RequiredGold(ceremony.Tier, ceremony.WaitIndex);
            if (requiredGold is null)
                return await FailAndExpireAsync(ceremony, relation, MarriageCeremonyFailure.Invalid, ct).ConfigureAwait(false);
            if (!await eligibility.CanAffordAsync(applicant.CharacterId, requiredGold.Value, ct).ConfigureAwait(false))
                return await FailAndExpireAsync(ceremony, relation, MarriageCeremonyFailure.InsufficientFunds, ct).ConfigureAwait(false);
        }
        var state = response switch
        {
            0 => MarriageCeremonyState.Reserved,
            1 => MarriageCeremonyState.Rejected,
            _ => MarriageCeremonyState.Expired,
        };
        if (!await reservations.TransitionAsync(ceremony, state, response, ct).ConfigureAwait(false))
            return new(MarriageCeremonyFailure.Stale);
        await ledger.MustCommitAsync(relation, response == 0 ? MarriageTrigger.AcceptCeremony : MarriageTrigger.CeremonyWithdrawn,
            null, null, ct).ConfigureAwait(false);
        return new(MarriageCeremonyFailure.None, ceremony with
        {
            State = state,
            Revision = ceremony.Revision + 1,
            Response = response,
        }, true);
    }

    public async ValueTask<IReadOnlyList<MarriageWeddingAdmission>> OpenWeddingAsync(MarriageCeremony ceremony, CancellationToken ct)
    {
        if (await settlement.ReadAsync(ceremony.Id, ct).ConfigureAwait(false) is not null) return [];
        if (await ledger.ReadAsync(ceremony.RelationId, ct).ConfigureAwait(false) is not { Stage: MarriageStage.Preparing } relation
            || relation.CeremonyId != ceremony.Id) throw new MarriageWeddingUnavailableException();
        var rule = settlement.Rules(ceremony.Tier);
        var result = new List<MarriageWeddingAdmission>();
        foreach (var original in new[] { ceremony.Applicant, ceremony.Partner })
        {
            var actor = eligibility.SessionOf(original.CharacterId);
            if (actor != original || await ReservedAsync(relation, actor, ct).ConfigureAwait(false) is not { } reserved
                || await church.PrepareAsync(actor, reserved.Ceremony, ct).ConfigureAwait(false) is not { } prepared)
                throw new MarriageWeddingUnavailableException();
            var admitted = await church.EnterNewlywedAsync(actor, reserved.Ceremony, checked((uint)prepared.Peer.Value),
                prepared.Room.AreaId, ct).ConfigureAwait(false);
            if (!admitted.Succeeded) throw new MarriageWeddingUnavailableException();
            result.Add(new(actor, prepared));
        }
        await settlement.OpenAsync(ceremony, rule, ct).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<MarriageLovePointResult> UseLovePointAsync(SelectedSessionIdentity actor, BusinessSlot slot,
        IReadOnlyList<AccountId> accounts, CancellationToken ct)
    {
        if (!eligibility.IsCurrent(actor) || !LovePoints.Carries(slot)) return new(-1);
        var found = await ledger.ForSessionAsync(actor, ct).ConfigureAwait(false);
        if (!accounts.SequenceEqual(CoupleAccounts(found, actor))) return new(-1);
        if (found is not { Stage: MarriageStage.Married } relation) return new(-3);
        var (status, spend) = await love.SpendAsync(actor, slot, relation.LovePoints, ct).ConfigureAwait(false);
        if (spend is null) return new(status);
        if (!await ledger.AddLovePointsAsync(relation, spend.Amount, spend.Maximum, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Love point settlement changed under the protected account transaction.");
        return new(0, relation.LovePoints + spend.Amount, PartnerOf(relation, actor.CharacterId).CharacterId)
        { State = await love.RemainingAsync(spend, ct).ConfigureAwait(false) };
    }

    private async ValueTask<MarriageCeremonyResult> FailAndExpireAsync(MarriageCeremony ceremony, MarriageRelation relation,
        MarriageCeremonyFailure failure, CancellationToken ct)
    {
        if (!await reservations.TransitionAsync(ceremony, MarriageCeremonyState.Expired, 2, ct).ConfigureAwait(false))
            return new(MarriageCeremonyFailure.Stale);
        await ledger.MustCommitAsync(relation, MarriageTrigger.CeremonyWithdrawn, null, null, ct).ConfigureAwait(false);
        return new(failure, ceremony with
        {
            State = MarriageCeremonyState.Expired,
            Revision = ceremony.Revision + 1,
            Response = 2,
        }, true);
    }

    /// <summary>Both have cleared the trial and material quests, and every one of those acceptances is bound to this relation.</summary>
    private async ValueTask<bool> WeddingQuestsReadyAsync(Guid relation, SelectedSessionIdentity first,
        SelectedSessionIdentity second, CancellationToken ct) =>
        await eligibility.WeddingQuestsClearedAsync(first, second, ct).ConfigureAwait(false)
        && await trials.BoundToAsync(relation, [first.CharacterId, second.CharacterId], ct).ConfigureAwait(false);
}
