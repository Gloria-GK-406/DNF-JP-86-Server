using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Marriage;

/// <summary>The persisted relations: stage as stored, the proposal records behind them, and who belongs to which.</summary>
/// <remarks>
/// Whether sessions belong to a relation is answered only here: <see cref="ForSessionAsync"/>, <see cref="ForParticipantAsync"/>,
/// <see cref="ForPairAsync"/>, or <see cref="Belongs(MarriageRelation, SelectedSessionIdentity)"/> on a relation already read by id.
/// <see cref="FindAsync"/> skips the account and is only for lookups by a client-supplied character key.
/// The stage is the stored one; projecting a lapsed proposal or reservation is the marriage query's business.
/// </remarks>
internal interface IMarriageRelations
{
    ValueTask<int> PointsAsync(Guid relation, CharacterId character, CancellationToken ct);
    ValueTask<byte> RingAsync(Guid relation, CancellationToken ct);
    ValueTask<MarriageRelation?> FindAsync(CharacterId character, CancellationToken ct);

    ValueTask<MarriageRelation?> ReadAsync(Guid relation, CancellationToken ct);

    ValueTask<MarriageRelation?> ForSessionAsync(SelectedSessionIdentity session, CancellationToken ct);

    /// <summary>The character's active relation, when that participant is the account's and the relation is not closed.</summary>
    ValueTask<MarriageRelation?> ForParticipantAsync(AccountId account, CharacterId character, CancellationToken ct);

    /// <summary>The relation both sessions belong to as its two different participants.</summary>
    ValueTask<MarriageRelation?> ForPairAsync(SelectedSessionIdentity first, SelectedSessionIdentity second, CancellationToken ct);

    ValueTask<MarriageProposal?> FindProposalAsync(CharacterId character, CancellationToken ct);

    ValueTask<MarriageProposal?> ReceivedProposalAsync(CharacterId character, CancellationToken ct);

    ValueTask<int> CountUnsettledReservationsAsync(CancellationToken ct);

    static bool Belongs(MarriageRelation relation, SelectedSessionIdentity session) =>
        Belongs(relation, session.AccountId, session.CharacterId);

    static bool Belongs(MarriageRelation relation, AccountId account, CharacterId character) =>
        relation.Stage != MarriageStage.Closed
        && (relation.Proposer.AccountId == account && relation.Proposer.CharacterId == character
            || relation.Recipient.AccountId == account && relation.Recipient.CharacterId == character);

    static bool IsPair(MarriageRelation relation, SelectedSessionIdentity first, SelectedSessionIdentity second) =>
        first.CharacterId != second.CharacterId && Belongs(relation, first) && Belongs(relation, second);
}

/// <summary>The relation writes: creating a relation with its proposal, moving the stage by the transition table, adding love points.</summary>
/// <remarks>Only the marriage use cases take this; the pre-wedding quests read through <see cref="IMarriageRelations"/>.</remarks>
internal interface IMarriageRelationWriter : IMarriageRelations
{
    ValueTask<bool> ChangePointsAsync(Guid relation, CharacterId character, int amount, CancellationToken ct);
    ValueTask<bool> DailyPointAsync(Guid relation, CharacterId character, long period, CancellationToken ct);
    ValueTask<bool> UpgradeRingAsync(Guid relation, byte expected, CancellationToken ct);
    /// <summary>T1: the pending proposal and its relation, created together.</summary>
    ValueTask<MarriageProposal> ProposeAsync(SelectedSessionIdentity proposer, SelectedSessionIdentity recipient, CancellationToken ct);

    ValueTask<bool> TransitionProposalAsync(MarriageProposal expected, MarriageProposalState state, byte? response, CancellationToken ct);

    /// <summary>For callers that already changed a child record: a refused commit must undo the whole use case.</summary>
    /// <returns>The relation as committed, for the rest of the use case to carry on with.</returns>
    ValueTask<MarriageRelation> MustCommitAsync(MarriageRelation? relation, MarriageTrigger trigger, Guid? ceremony,
        DateTimeOffset? marriedAt, CancellationToken ct);

}

/// <summary>The only writer of a relation's stage and love points, and the owner of the proposal records behind it.</summary>
internal sealed class CoupleLedger(IMarriageRelationDomainStore store, IMarriageProposalDomainStore proposals,
    IMarriageProposalRuleService rules, TimeProvider clock) : IMarriageRelationWriter
{
    public ValueTask<int> PointsAsync(Guid relation, CharacterId character, CancellationToken ct) => store.PointsAsync(relation, character, ct);
    public ValueTask<byte> RingAsync(Guid relation, CancellationToken ct) => store.RingAsync(relation, ct);
    public ValueTask<bool> ChangePointsAsync(Guid relation, CharacterId character, int amount, CancellationToken ct) => store.ChangePointsAsync(relation, character, amount, ct);
    public ValueTask<bool> DailyPointAsync(Guid relation, CharacterId character, long period, CancellationToken ct) => store.DailyPointAsync(relation, character, period, ct);
    public ValueTask<bool> UpgradeRingAsync(Guid relation, byte expected, CancellationToken ct) => store.UpgradeRingAsync(relation, expected, ct);
    public ValueTask<MarriageRelation?> FindAsync(CharacterId character, CancellationToken ct) => store.FindActiveAsync(character, ct);

    public ValueTask<MarriageRelation?> ReadAsync(Guid relation, CancellationToken ct) => store.ReadAsync(relation, ct);

    public ValueTask<MarriageRelation?> ForSessionAsync(SelectedSessionIdentity session, CancellationToken ct) =>
        ForParticipantAsync(session.AccountId, session.CharacterId, ct);

    public async ValueTask<MarriageRelation?> ForParticipantAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        await store.FindActiveAsync(character, ct).ConfigureAwait(false) is { } found && IMarriageRelations.Belongs(found, account, character)
            ? found : null;

    public async ValueTask<MarriageRelation?> ForPairAsync(SelectedSessionIdentity first, SelectedSessionIdentity second,
        CancellationToken ct)
    {
        if (first.CharacterId == second.CharacterId
            || await ForSessionAsync(first, ct).ConfigureAwait(false) is not { } relation) return null;
        return (await ForSessionAsync(second, ct).ConfigureAwait(false))?.Id == relation.Id ? relation : null;
    }

    public ValueTask<MarriageProposal?> FindProposalAsync(CharacterId character, CancellationToken ct) => proposals.FindActiveAsync(character, ct);

    public ValueTask<MarriageProposal?> ReceivedProposalAsync(CharacterId character, CancellationToken ct) =>
        proposals.FindLatestReceivedAsync(character, ct);

    public async ValueTask<MarriageProposal> ProposeAsync(SelectedSessionIdentity proposer, SelectedSessionIdentity recipient,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var proposal = new MarriageProposal(Guid.NewGuid(), proposer, recipient,
            MarriageProposalState.Pending, 1, now, now + rules.Lifetime);
        await proposals.CreateAsync(proposal, ct).ConfigureAwait(false);
        await store.CreateAsync(proposal.Id, MarriageStageRules.Next(null, MarriageTrigger.Propose)!.Value, clock.GetUtcNow(), ct)
            .ConfigureAwait(false);
        return proposal;
    }

    public ValueTask<bool> TransitionProposalAsync(MarriageProposal expected, MarriageProposalState state, byte? response,
        CancellationToken ct) => proposals.TransitionAsync(expected, state, response, ct);

    /// <returns>False when the table rejects the trigger or the relation changed since it was read.</returns>
    /// <remarks>Only tests call it, so it is not on <see cref="IMarriageRelationWriter"/>; use cases commit through <see cref="MustCommitAsync"/>.</remarks>
    public async ValueTask<bool> CommitAsync(MarriageRelation relation, MarriageTrigger trigger, Guid? ceremony,
        DateTimeOffset? marriedAt, CancellationToken ct) =>
        await TryCommitAsync(relation, trigger, ceremony, marriedAt, ct).ConfigureAwait(false) is not null;

    public async ValueTask<MarriageRelation> MustCommitAsync(MarriageRelation? relation, MarriageTrigger trigger, Guid? ceremony,
        DateTimeOffset? marriedAt, CancellationToken ct) =>
        (relation is null ? null : await TryCommitAsync(relation, trigger, ceremony, marriedAt, ct).ConfigureAwait(false))
        ?? throw new InvalidOperationException($"Marriage relation {relation?.Id} could not take {trigger}.");

    private async ValueTask<MarriageRelation?> TryCommitAsync(MarriageRelation relation, MarriageTrigger trigger, Guid? ceremony,
        DateTimeOffset? marriedAt, CancellationToken ct)
    {
        if (MarriageStageRules.Next(relation.Stage, trigger) is not { } stage) return null;
        var reference = stage switch
        {
            MarriageStage.Requested => ceremony ?? throw new ArgumentNullException(nameof(ceremony)),
            MarriageStage.Preparing or MarriageStage.Married => relation.CeremonyId,
            _ => null,
        };
        var at = clock.GetUtcNow();
        var married = stage == MarriageStage.Married ? marriedAt : null;
        if (!await store.TransitionAsync(relation, stage, reference, at, married, ct).ConfigureAwait(false)) return null;
        return relation with
        {
            Stage = stage, Revision = relation.Revision + 1, CeremonyId = reference, StageSince = at,
            MarriedAt = married ?? relation.MarriedAt,
        };
    }


    public ValueTask<int> CountUnsettledReservationsAsync(CancellationToken ct) => store.CountUnsettledReservationsAsync(ct);
}

internal sealed class MarriageRelationAudit(IMarriageRelations ledger, ILogger<MarriageRelationAudit> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var unsettled = await ledger.CountUnsettledReservationsAsync(cancellationToken).ConfigureAwait(false);
        if (unsettled > 0)
            logger.LogWarning("{Count} engaged marriage relations hold a Reserved ceremony without a live wedding; left Engaged.", unsettled);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
