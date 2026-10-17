using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>The marriage entity: every marriage use case, composed from components that never call each other.</summary>
/// <remarks>
/// Each public operation resolves its relation through the ledger once and hands that copy down; whether sessions are
/// a pair is always the ledger's answer.
/// </remarks>
internal sealed partial class MarriageDomainService(CoupleLedger ledger, MarriageEligibility eligibility, ReservationDesk reservations,
    ChurchRooms church, WeddingSettlement settlement, GuestList guests, Guestbook guestbook, TrialQuests trials, LovePoints love,
    TimeProvider clock)
    : IMarriageQueryDomainService, IMarriageCoupleDomainService, IMarriageVenueDomainService, IMarriageQuestDomainService
{
    /// <summary>The actor's preparing relation and its reservation.</summary>
    private async ValueTask<(MarriageRelation Relation, MarriageCeremony Ceremony)?> ReservedAsync(SelectedSessionIdentity actor, CancellationToken ct) =>
        eligibility.IsCurrent(actor)
            ? await ReservedAsync(await ledger.ForSessionAsync(actor, ct).ConfigureAwait(false), actor, ct).ConfigureAwait(false)
            : null;

    /// <summary>The same check on a relation the operation has already resolved.</summary>
    private async ValueTask<(MarriageRelation Relation, MarriageCeremony Ceremony)?> ReservedAsync(MarriageRelation? relation,
        SelectedSessionIdentity actor, CancellationToken ct)
    {
        if (relation is not { Stage: MarriageStage.Preparing } || !eligibility.IsCurrent(actor)
            || !CoupleLedger.Belongs(relation, actor)) return null;
        var ceremony = await reservations.ActiveAsync(relation.Id, ct).ConfigureAwait(false);
        if (ceremony is not { Tier: 0 } || ceremony.Id != relation.CeremonyId) return null;
        static bool SameCharacter(SelectedSessionIdentity first, SelectedSessionIdentity second) =>
            first.AccountId == second.AccountId && first.CharacterId == second.CharacterId;
        var samePair = SameCharacter(ceremony.Applicant, relation.Proposer) && SameCharacter(ceremony.Partner, relation.Recipient)
            || SameCharacter(ceremony.Applicant, relation.Recipient) && SameCharacter(ceremony.Partner, relation.Proposer);
        return samePair ? (relation, ceremony) : null;
    }

    /// <summary>The character's relation when the account owns that participant and the pair is engaged or further.</summary>
    private async ValueTask<MarriageRelation?> QuestRelationAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        await ledger.ForParticipantAsync(account, character, ct).ConfigureAwait(false) is { } relation
        && MarriageStageRules.IsCouple(relation.Stage) ? relation : null;

    /// <summary>Both accounts of the actor's couple, or only the actor's own when the actor belongs to no engaged relation.</summary>
    private static IReadOnlyList<AccountId> CoupleAccounts(MarriageRelation? relation, SelectedSessionIdentity actor) =>
        relation is not null && MarriageStageRules.IsCouple(relation.Stage)
            ? new[] { relation.Proposer.AccountId, relation.Recipient.AccountId }.Distinct().OrderBy(x => x.Value).ToArray()
            : [actor.AccountId];

    /// <summary>The character's active proposal, after settling it as lapsed when it timed out or a session ended.</summary>
    private async ValueTask<MarriageProposal?> ActiveProposalAsync(CharacterId character, CancellationToken ct)
    {
        var active = await ledger.FindProposalAsync(character, ct).ConfigureAwait(false);
        if (active is { State: MarriageProposalState.Pending } && ProposalLapsed(active))
        {
            var relation = await ledger.ReadAsync(active.Id, ct).ConfigureAwait(false);
            if (!await ledger.TransitionProposalAsync(active, MarriageProposalState.Expired, 2, ct).ConfigureAwait(false))
                return await ledger.FindProposalAsync(character, ct).ConfigureAwait(false);
            await ledger.MustCommitAsync(relation, MarriageTrigger.ProposalLapsed, null, null, ct).ConfigureAwait(false);
            return null;
        }
        return active;
    }

    /// <summary>The relation's live reservation, after settling a pending one as withdrawn when it timed out or a session ended.</summary>
    /// <returns>The reservation, and the relation as it stands afterwards.</returns>
    private async ValueTask<(MarriageCeremony? Ceremony, MarriageRelation Relation)> ActiveReservationAsync(MarriageRelation relation,
        CancellationToken ct)
    {
        var active = await reservations.ActiveAsync(relation.Id, ct).ConfigureAwait(false);
        if (active is not { State: MarriageCeremonyState.Pending }) return (active, relation);
        if (!ReservationLapsed(active)) return (active, relation);
        if (!await reservations.TransitionAsync(active, MarriageCeremonyState.Expired, 2, ct).ConfigureAwait(false))
            return (await reservations.ActiveAsync(relation.Id, ct).ConfigureAwait(false), relation);
        return (null, await ledger.MustCommitAsync(relation, MarriageTrigger.CeremonyWithdrawn, null, null, ct).ConfigureAwait(false));
    }

    private bool ProposalLapsed(MarriageProposal proposal) => proposal.ExpiresAt <= clock.GetUtcNow()
        || !eligibility.IsCurrent(proposal.Proposer) || !eligibility.IsCurrent(proposal.Recipient);

    private bool ReservationLapsed(MarriageCeremony ceremony) => !(ceremony.ExpiresAt > clock.GetUtcNow()
        && eligibility.IsCurrent(ceremony.Applicant) && eligibility.IsCurrent(ceremony.Partner));

    private static SelectedSessionIdentity PartnerOf(MarriageRelation relation, CharacterId character) =>
        relation.Proposer.CharacterId == character ? relation.Recipient : relation.Proposer;

    private static CharacterId PeerOf(MarriageCeremony ceremony, CharacterId character) =>
        ceremony.Applicant.CharacterId == character ? ceremony.Partner.CharacterId : ceremony.Applicant.CharacterId;
}
