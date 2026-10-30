using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    public DungeonCardLayoutChange? TryOpenCardLayout(
        CharacterId characterId,
        DungeonRunId expectedRunId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryOpenCardLayout));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId)
            {
                return null;
            }

            var progress = aggregate.ProgressFor(characterId);
            if (!progress.IsSettled
                || progress.ClearRewardCards is null
                || progress.CardLayoutOpen)
            {
                return null;
            }

            progress.CardLayoutOpen = true;
            aggregate.RuntimeRevision++;
            return new DungeonCardLayoutChange(
                aggregate.RunId,
                aggregate.RuntimeRevision,
                characterId,
                [characterId]);
        }
    }

    public DungeonRunState? FixClearRewardCards(
        CharacterId characterId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(FixClearRewardCards));
        return this.FixClearRewardCardsCore(
            characterId, expectedRunId: null, free, paid, paidFlipCost);
    }

    public DungeonRunState? FixClearRewardCards(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost,
        ClearRewardCardFace? blackDiamond = null)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(FixClearRewardCards));
        return this.FixClearRewardCardsCore(characterId, expectedRunId, free, paid, paidFlipCost, blackDiamond);
    }

    private DungeonRunState? FixClearRewardCardsCore(
        CharacterId characterId,
        DungeonRunId? expectedRunId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost,
        ClearRewardCardFace? blackDiamond = null)
    {
        ArgumentNullException.ThrowIfNull(free);
        ArgumentNullException.ThrowIfNull(paid);
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate)
                return null;
            if (expectedRunId is { } runId && aggregate.RunId != runId)
                return null;

            var progress = aggregate.ProgressFor(characterId);
            progress.ClearRewardCards ??= new ClearRewardCardSet(
                free, paid, ImmutableHashSet<ClearRewardCardId>.Empty, paidFlipCost)
            { BlackDiamond = blackDiamond };
            return aggregate.ViewFor(characterId);
        }
    }

    public ClearRewardCardSlot? TryClaimClearRewardCard(
        CharacterId characterId,
        ClearRewardCardKind kind,
        int index)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryClaimClearRewardCard));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate)
                return null;

            var progress = aggregate.ProgressFor(characterId);
            if (progress.ClearRewardCards is not { } cards
                || cards.SlotAt(kind, index) is not { } slot)
            {
                return null;
            }

            var id = new ClearRewardCardId(kind, index);
            if (cards.Claimed.Contains(id))
                return null;

            progress.ClearRewardCards = cards with { Claimed = cards.Claimed.Add(id) };
            return slot;
        }
    }

    public DungeonClearRewardCardClaim? TryReserveClearRewardCard(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        ClearRewardCardKind kind,
        int index)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryReserveClearRewardCard));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId)
            {
                return null;
            }

            var progress = aggregate.ProgressFor(characterId);
            var card = new ClearRewardCardId(kind, index);
            if (progress.ClearRewardCards is not { } cards
                || !progress.IsSettled
                || !progress.CardLayoutOpen
                || cards.SlotAt(kind, index) is not { } slot
                || cards.Claimed.Contains(card)
                || aggregate.CardSelections.Any(selection =>
                    selection.Kind == kind && selection.CharacterId == characterId))
            {
                return null;
            }

            var key = new ClearRewardCardClaimKey(aggregate.RunId, characterId, card);
            if (this.preparedClearRewardCardClaims.Contains(key)
                || this.clearRewardCardClaims.ContainsKey(key)
                || this.clearRewardCardClaims.Any(entry =>
                    entry.Key.RunId == aggregate.RunId
                    && entry.Key.Card.Kind == kind
                    && entry.Value.CharacterId == characterId))
            {
                return null;
            }

            var token = Interlocked.Increment(ref this.nextClaim);
            this.clearRewardCardClaims.Add(key, new ClearRewardReservation(characterId, token));
            return new DungeonClearRewardCardClaim(
                characterId,
                aggregate.ViewFor(characterId),
                card,
                slot,
                token,
                aggregate.RunId);
        }
    }

    public DungeonCardSelectionCommit CommitClearRewardCard(
        DungeonClearRewardCardClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(CommitClearRewardCard));
        ArgumentNullException.ThrowIfNull(claim);
        var transaction = this.transactions.Current;
        if (transaction is null)
        {
            var prepared = this.TryPrepareClearRewardCard(claim);
            if (prepared is null)
            {
                _ = this.ReleaseClearRewardCard(claim);
                return DungeonCardSelectionCommit.Completed(null);
            }

            return DungeonCardSelectionCommit.Completed(
                this.CommitPreparedClearRewardCard(prepared));
        }

        var participant = transaction.Enlist(
            new ClearRewardCardCommitIdentity(
                this, claim.RunId, claim.Card, claim.CharacterId, claim.Token),
            () => new ClearRewardCardCommitParticipant(this, claim));
        return participant.Receipt;
    }

    private PreparedClearRewardCard? TryPrepareClearRewardCard(
        DungeonClearRewardCardClaim claim)
    {
        lock (this.gate)
        {
            var key = new ClearRewardCardClaimKey(claim.RunId, claim.CharacterId, claim.Card);
            if (!this.clearRewardCardClaims.TryGetValue(key, out var reservation)
                || reservation.CharacterId != claim.CharacterId
                || reservation.Token != claim.Token
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || !aggregate.IsMember(claim.CharacterId)
                || this.preparedClearRewardCardClaims.Contains(key))
            {
                return null;
            }

            var progress = aggregate.ProgressFor(claim.CharacterId);
            if (progress.ClearRewardCards is not { } cards
                || !progress.IsSettled
                || !progress.CardLayoutOpen
                || cards.Claimed.Contains(claim.Card)
                || aggregate.CardSelections.Any(selection =>
                    selection.Kind == claim.Card.Kind
                    && selection.CharacterId == claim.CharacterId)
                || !this.preparedClearRewardCardClaims.Add(key))
            {
                return null;
            }

            return new PreparedClearRewardCard(
                key, claim.CharacterId, claim.Token, aggregate,
                aggregate.Participants.Single(participant =>
                    participant.CharacterId == claim.CharacterId).Slot);
        }
    }

    private DungeonCardSelectionChange CommitPreparedClearRewardCard(
        PreparedClearRewardCard prepared)
    {
        lock (this.gate)
        {
            // Prepare captured the exact run and reservation. Durable inventory/gold commits first;
            // this process-local publication is deliberately non-failing afterwards, even if the
            // participant disconnected and the aggregate left the live directory in between.
            _ = this.TryRemoveClearRewardCardClaim(prepared.Key, prepared.Token);
            _ = this.preparedClearRewardCardClaims.Remove(prepared.Key);
            var progress = prepared.Aggregate.ProgressFor(prepared.CharacterId);
            var cards = progress.ClearRewardCards
                ?? throw new InvalidOperationException(
                    "A prepared clear-card commit lost its frozen card layout.");
            progress.ClearRewardCards = cards with
            {
                Claimed = cards.Claimed.Add(prepared.Key.Card),
            };
            prepared.Aggregate.CardSelections.Add(new DungeonCardSelection(
                prepared.Key.Card.Kind,
                prepared.Slot,
                prepared.CharacterId,
                prepared.Slot)
            {
                Gold = cards.FaceOf(prepared.Key.Card.Kind).Gold,
                Item = cards.SlotAt(prepared.Key.Card.Kind, prepared.Key.Card.Index)?.Item,
            });
            // A free-card confirmation is private until every current participant has confirmed.
            // The wire index is not the shared display column; the party seat is.
            var ready = prepared.Key.Card.Kind is ClearRewardCardKind.Paid
                || prepared.Aggregate.Participants.All(participant =>
                    prepared.Aggregate.CardSelections.Any(selection =>
                        selection.Kind is ClearRewardCardKind.Free
                        && selection.CharacterId == participant.CharacterId));
            if (ready)
                prepared.Aggregate.RevealedCardKinds.Add(prepared.Key.Card.Kind);
            prepared.Aggregate.RuntimeRevision++;
            return new DungeonCardSelectionChange(
                prepared.Aggregate.RunId,
                prepared.Aggregate.RuntimeRevision,
                prepared.CharacterId,
                ready ? prepared.Aggregate.PublishedCardSelections() : [],
                [.. prepared.Aggregate.Participants
                    .Where(participant =>
                    {
                        var participantProgress = prepared.Aggregate.ProgressFor(
                            participant.CharacterId);
                        return participantProgress.IsSettled && participantProgress.CardLayoutOpen;
                    })
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)]);
        }
    }

    private void RollbackPreparedClearRewardCard(
        DungeonClearRewardCardClaim claim,
        PreparedClearRewardCard? prepared)
    {
        lock (this.gate)
        {
            if (prepared is { } value)
                _ = this.preparedClearRewardCardClaims.Remove(value.Key);
            _ = this.TryRemoveClearRewardCardClaim(
                new ClearRewardCardClaimKey(claim.RunId, claim.CharacterId, claim.Card), claim.Token);
        }
    }

    public bool ReleaseClearRewardCard(DungeonClearRewardCardClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(ReleaseClearRewardCard));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new ClearRewardCardClaimKey(claim.RunId, claim.CharacterId, claim.Card);
            return !this.preparedClearRewardCardClaims.Contains(key)
                && this.TryRemoveClearRewardCardClaim(key, claim.Token);
        }
    }

    private bool TryRemoveClearRewardCardClaim(ClearRewardCardClaimKey key, long token) =>
        this.clearRewardCardClaims.TryGetValue(key, out var current)
        && current.Token == token
        && this.clearRewardCardClaims.Remove(key);

    private void ClearClearRewardCardClaims(DungeonRunId runId)
    {
        foreach (var key in this.clearRewardCardClaims.Keys
                     .Where(key => key.RunId == runId
                         && !this.preparedClearRewardCardClaims.Contains(key)).ToArray())
        {
            this.clearRewardCardClaims.Remove(key);
        }
    }
    private readonly record struct ClearRewardCardClaimKey(
        DungeonRunId RunId,
        CharacterId CharacterId,
        ClearRewardCardId Card);
    private readonly record struct ClearRewardReservation(CharacterId CharacterId, long Token);
    private readonly record struct ClearRewardCardCommitIdentity(
        DungeonRunDomainService Owner,
        DungeonRunId RunId,
        ClearRewardCardId Card,
        CharacterId CharacterId,
        long Token);
    private sealed record PreparedClearRewardCard(
        ClearRewardCardClaimKey Key,
        CharacterId CharacterId,
        long Token,
        DungeonRunAggregate Aggregate,
        byte Slot);

    private sealed class ClearRewardCardCommitParticipant(
        DungeonRunDomainService owner,
        DungeonClearRewardCardClaim claim) : ITransactionParticipant
    {
        private PreparedClearRewardCard? prepared;

        public bool CommitCanFail => false;

        public DungeonCardSelectionChange? Change { get; private set; }

        public DungeonCardSelectionCommit Receipt => new(() => this.Change);

        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.prepared = owner.TryPrepareClearRewardCard(claim)
                ?? throw new InvalidOperationException(
                    "The clear-card reservation changed before its transaction prepared.");
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            this.Change = owner.CommitPreparedClearRewardCard(
                this.prepared ?? throw new InvalidOperationException(
                    "A clear-card commit must be prepared before it is applied."));
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            owner.RollbackPreparedClearRewardCard(claim, this.prepared);
            return ValueTask.CompletedTask;
        }
    }
}
