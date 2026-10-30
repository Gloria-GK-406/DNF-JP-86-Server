using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunStore
{
    internal sealed partial class CardService
    {
        public DungeonCardLayoutChange? TryOpenCardLayout(
            CharacterId characterId,
            DungeonRunId expectedRunId)
        {
            GameplayWriteAssertion.Writing(nameof(CardService) + "." + nameof(TryOpenCardLayout));
            lock (this.store.gate)
            {
                if (this.store.RunOf(characterId) is not { } aggregate
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
            GameplayWriteAssertion.Writing(nameof(CardService) + "." + nameof(FixClearRewardCards));
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
            GameplayWriteAssertion.Writing(nameof(CardService) + "." + nameof(FixClearRewardCards));
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
            lock (this.store.gate)
            {
                if (this.store.RunOf(characterId) is not { } aggregate)
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
            GameplayWriteAssertion.Writing(nameof(CardService) + "." + nameof(TryClaimClearRewardCard));
            lock (this.store.gate)
            {
                if (this.store.RunOf(characterId) is not { } aggregate)
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
            GameplayWriteAssertion.Writing(nameof(CardService) + "." + nameof(TryReserveClearRewardCard));
            lock (this.store.gate)
            {
                if (this.store.RunOf(characterId) is not { } aggregate
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

                var key = new ClearRewardCardClaimKey(characterId, card);
                if (aggregate.Claims.PreparedCards.Contains(key)
                    || aggregate.Claims.Cards.ContainsKey(key)
                    || aggregate.Claims.Cards.Any(entry =>
                        entry.Key.Card.Kind == kind
                        && entry.Value.CharacterId == characterId))
                {
                    return null;
                }

                var token = this.store.NextClaim();
                aggregate.Claims.Cards.Add(key, new ClearRewardReservation(characterId, token));
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
            GameplayWriteAssertion.Writing(nameof(CardService) + "." + nameof(CommitClearRewardCard));
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
            lock (this.store.gate)
            {
                var key = new ClearRewardCardClaimKey(claim.CharacterId, claim.Card);
                if (!this.store.runsById.TryGetValue(claim.RunId, out var aggregate)
                    || !aggregate.Claims.Cards.TryGetValue(key, out var reservation)
                    || reservation.CharacterId != claim.CharacterId
                    || reservation.Token != claim.Token
                    || !aggregate.IsMember(claim.CharacterId)
                    || aggregate.Claims.PreparedCards.Contains(key))
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
                    || !aggregate.Claims.PreparedCards.Add(key))
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
            lock (this.store.gate)
            {
                // Prepare captured the exact run and reservation. Durable inventory/gold commits first;
                // this process-local publication is deliberately non-failing afterwards, even if the
                // participant disconnected and the aggregate left the live directory in between.
                _ = TryRemoveClearRewardCardClaim(prepared.Aggregate.Claims, prepared.Key, prepared.Token);
                _ = prepared.Aggregate.Claims.PreparedCards.Remove(prepared.Key);
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
            lock (this.store.gate)
            {
                if (prepared is { } value)
                    _ = value.Aggregate.Claims.PreparedCards.Remove(value.Key);
                if (this.store.ClaimsOf(claim.RunId) is { } claims)
                    _ = TryRemoveClearRewardCardClaim(claims, new ClearRewardCardClaimKey(claim.CharacterId, claim.Card), claim.Token);
            }
        }

        public bool ReleaseClearRewardCard(DungeonClearRewardCardClaim claim)
        {
            GameplayWriteAssertion.Writing(nameof(CardService) + "." + nameof(ReleaseClearRewardCard));
            ArgumentNullException.ThrowIfNull(claim);
            lock (this.store.gate)
            {
                var key = new ClearRewardCardClaimKey(claim.CharacterId, claim.Card);
                return this.store.ClaimsOf(claim.RunId) is { } claims
                    && !claims.PreparedCards.Contains(key)
                    && TryRemoveClearRewardCardClaim(claims, key, claim.Token);
            }
        }

        private static bool TryRemoveClearRewardCardClaim(RunClaims claims, ClearRewardCardClaimKey key, long token) =>
            claims.Cards.TryGetValue(key, out var current)
            && current.Token == token
            && claims.Cards.Remove(key);

        private readonly record struct ClearRewardCardCommitIdentity(
            CardService Owner,
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
            CardService owner,
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
}
