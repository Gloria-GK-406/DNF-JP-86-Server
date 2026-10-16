using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed class MarriageProposalDomainService(IMarriageProposalDomainStore store,
    IMarriageProposalRuleService rules, ICharacterDomainService characters,
    ISelectedSessionDomainService sessions, ITownAreaDomainService areas, TimeProvider clock)
    : IMarriageProposalDomainService
{
    public ValueTask<MarriageProposal?> FindReceivedAsync(CharacterId character, CancellationToken ct) =>
        store.FindLatestReceivedAsync(character, ct);

    public async ValueTask<MarriageProposalResult> ProposeAsync(SelectedSessionIdentity proposer,
        SelectedSessionIdentity recipient, CancellationToken ct)
    {
        if (!Current(proposer) || !Current(recipient)) return new(MarriageProposalFailure.Stale);
        if (proposer.AccountId == recipient.AccountId || proposer.CharacterId == recipient.CharacterId)
            return new(MarriageProposalFailure.Invalid);
        if (!SameTown(proposer, recipient)) return new(MarriageProposalFailure.Unavailable);
        if (!await EligibleAsync(proposer, recipient, ct).ConfigureAwait(false)) return new(MarriageProposalFailure.Ineligible);
        var existing = await ActiveAsync(proposer.CharacterId, ct).ConfigureAwait(false);
        var other = await ActiveAsync(recipient.CharacterId, ct).ConfigureAwait(false);
        if (existing is { State: MarriageProposalState.Pending } && existing.Proposer == proposer
            && existing.Recipient == recipient && other?.Id == existing.Id) return new(MarriageProposalFailure.None, existing);
        if (existing is not null || other is not null) return new(MarriageProposalFailure.Occupied);
        var now = clock.GetUtcNow();
        var proposal = new MarriageProposal(Guid.NewGuid(), proposer, recipient,
            MarriageProposalState.Pending, 1, now, now + rules.Lifetime);
        await store.CreateAsync(proposal, ct).ConfigureAwait(false);
        return new(MarriageProposalFailure.None, proposal, true);
    }

    public async ValueTask<MarriageProposalResult> RespondAsync(SelectedSessionIdentity recipient,
        SelectedSessionIdentity proposer, byte response, CancellationToken ct)
    {
        if (response > 2) return new(MarriageProposalFailure.Invalid);
        var proposal = await store.FindLatestReceivedAsync(recipient.CharacterId, ct).ConfigureAwait(false);
        if (proposal is null || proposal.Proposer != proposer || proposal.Recipient != recipient
            || !Current(recipient) || !Current(proposer)) return new(MarriageProposalFailure.Stale);
        if (proposal.State != MarriageProposalState.Pending)
            return proposal.Response == response ? new(MarriageProposalFailure.None, proposal)
                : new(MarriageProposalFailure.Stale);
        if (proposal.ExpiresAt <= clock.GetUtcNow())
        {
            var changed = await store.TransitionAsync(proposal, MarriageProposalState.Expired, 2, ct).ConfigureAwait(false);
            return response == 2 && changed ? new(MarriageProposalFailure.None,
                proposal with { State = MarriageProposalState.Expired, Revision = proposal.Revision + 1, Response = 2 }, true)
                : new(MarriageProposalFailure.Stale);
        }
        if (response == 0 && !SameTown(proposer, recipient)) return new(MarriageProposalFailure.Unavailable);
        if (response == 0 && !await EligibleAsync(proposer, recipient, ct).ConfigureAwait(false)) return new(MarriageProposalFailure.Ineligible);
        var state = response switch { 0 => MarriageProposalState.Engaged, 1 => MarriageProposalState.Rejected, _ => MarriageProposalState.Expired };
        if (!await store.TransitionAsync(proposal, state, response, ct).ConfigureAwait(false))
            return new(MarriageProposalFailure.Stale);
        return new(MarriageProposalFailure.None, proposal with { State = state, Revision = proposal.Revision + 1, Response = response }, true);
    }

    private async ValueTask<MarriageProposal?> ActiveAsync(CharacterId character, CancellationToken ct)
    {
        var active = await store.FindActiveAsync(character, ct).ConfigureAwait(false);
        if (active is { State: MarriageProposalState.Pending } && (active.ExpiresAt <= clock.GetUtcNow()
            || !Current(active.Proposer) || !Current(active.Recipient)))
        {
            if (!await store.TransitionAsync(active, MarriageProposalState.Expired, 2, ct).ConfigureAwait(false))
                return await store.FindActiveAsync(character, ct).ConfigureAwait(false);
            return null;
        }
        return active;
    }
    private bool Current(SelectedSessionIdentity session) => sessions.Find(session.AccountId) == session;
    private bool SameTown(SelectedSessionIdentity first, SelectedSessionIdentity second) =>
        first.ListenerPort == second.ListenerPort && areas.Find(first.CharacterId) is { SharesTownPresence: true } a
        && areas.Find(second.CharacterId) is { SharesTownPresence: true } b && a.Area == b.Area
        && a.Lease == first.TownAreaLease() && b.Lease == second.TownAreaLease();
    private async ValueTask<bool> EligibleAsync(SelectedSessionIdentity firstSession, SelectedSessionIdentity secondSession, CancellationToken ct)
    {
        var first = await characters.Get(firstSession.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        var second = await characters.Get(secondSession.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        var a = await characters.Get(firstSession.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
        var b = await characters.Get(secondSession.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
        return first is not null && second is not null && a is not null && b is not null
            && a.Level >= rules.MinimumLevel && b.Level >= rules.MinimumLevel && rules.CanPair(first.Job, second.Job);
    }
}
