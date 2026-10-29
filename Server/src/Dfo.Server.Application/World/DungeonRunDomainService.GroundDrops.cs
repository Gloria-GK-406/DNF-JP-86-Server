using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    public DungeonGroundDropClaim? TryClaimGroundDrop(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        ushort groundDropNumber)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryClaimGroundDrop));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion)
            {
                return null;
            }

            if (aggregate.LostLandExploration?.HasExpired(this.clock.GetUtcNow()) == true) return null;
            var drop = new GroundDropId(aggregate.CurrentCell, groundDropNumber);
            var cell = aggregate.Cells[aggregate.CurrentCell];
            var item = cell.FindGroundItem(groundDropNumber);
            // Preserve A21's declared-object association, but never bank its reward before
            // the owning chest destruction has actually committed (reservation is insufficient).
            if (item?.Source is { Kind: DungeonGroundItemSourceKind.DeclaredObject, ObjectIndex: { } objectIndex }
                && cell.HasUnopenedRewardObject(aggregate.Layout.DungeonId, objectIndex, this.sealedTreasureRules))
                return null;
            var key = new GroundDropClaimKey(aggregate.RunId, drop, characterId);
            if (item is null
                || this.groundDropClaims.Keys.Any(candidate =>
                    candidate.RunId == aggregate.RunId && candidate.Drop == drop)
                || this.preparedGroundDropClaims.Any(candidate =>
                    candidate.RunId == aggregate.RunId && candidate.Drop == drop))
                return null;

            var token = Interlocked.Increment(ref this.nextClaim);
            this.groundDropClaims.Add(key, token);
            return new DungeonGroundDropClaim(
                characterId,
                aggregate.ViewFor(characterId),
                drop,
                token,
                aggregate.RunId,
                item,
                aggregate.RoomVersion,
                cell.Revision);
        }
    }

    public DungeonPickupChange? CommitGroundDrop(
        DungeonGroundDropClaim claim,
        DungeonPickupKind kind)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(CommitGroundDrop));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new GroundDropClaimKey(claim.RunId, claim.Drop, claim.CharacterId);
            if (!this.groundDropClaims.TryGetValue(key, out var token)
                || token != claim.Token
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || aggregate.CurrentCell != claim.Drop.Cell
                || aggregate.RoomVersion != claim.RoomVersion
                || !aggregate.Cells.TryGetValue(claim.Drop.Cell, out var cell)
                || cell.Revision != claim.CellRevision
                || cell.RemoveGroundItem(claim.Drop.Number) is null)
            {
                return null;
            }

            _ = this.groundDropClaims.Remove(key);
            aggregate.RuntimeRevision++;
            return new DungeonPickupChange(
                aggregate.RunId,
                aggregate.RuntimeRevision,
                claim.CharacterId,
                claim.Drop,
                kind,
                [.. aggregate.Participants
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)],
                aggregate.RoomVersion,
                cell.Revision);
        }
    }

    public DungeonPickupCommit StageGroundDrop(
        DungeonGroundDropClaim claim,
        DungeonPickupKind kind)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(StageGroundDrop));
        ArgumentNullException.ThrowIfNull(claim);
        var transaction = this.transactions?.Current;
        if (transaction is null)
            return DungeonPickupCommit.Completed(this.CommitGroundDrop(claim, kind));

        var participant = transaction.Enlist(
            new GroundDropCommitIdentity(
                this, claim.RunId, claim.Drop, claim.CharacterId, claim.Token),
            () => new GroundDropCommitParticipant(this, claim, kind));
        participant.RequireKind(kind);
        return participant.Receipt;
    }

    public bool ReleaseGroundDrop(DungeonGroundDropClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(ReleaseGroundDrop));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            return this.TryRemoveGroundDropClaim(
                new GroundDropClaimKey(claim.RunId, claim.Drop, claim.CharacterId), claim.Token);
        }
    }

    public DungeonGroundItemsAddedChange? TryAddGroundItems(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        long expectedCellRevision,
        IReadOnlyList<DungeonGroundItemDraft> items)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryAddGroundItems));
        ArgumentNullException.ThrowIfNull(items);
        lock (this.gate)
        {
            if (items.Count == 0
                || this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion)
            {
                return null;
            }

            if (aggregate.LostLandExploration?.HasExpired(this.clock.GetUtcNow()) == true) return null;
            var cell = aggregate.Cells[aggregate.CurrentCell];
            if (cell.Revision != expectedCellRevision)
                return null;
            var eligible = items.Where(item => item.OwnerCharacterId is not { } owner
                || aggregate.IsMember(owner)).ToArray();
            if (eligible.Length == 0)
                return null;
            var added = cell.AppendGroundItems(eligible);
            cell.Revision++;
            aggregate.RuntimeRevision++;
            var run = aggregate.ViewFor(characterId);
            return new DungeonGroundItemsAddedChange(
                run,
                aggregate.RuntimeRevision,
                cell.Revision,
                added,
                [.. aggregate.Participants
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)]);
        }
    }

    private bool TryRemoveGroundDropClaim(GroundDropClaimKey key, long token) =>
        this.groundDropClaims.TryGetValue(key, out var current)
        && current == token
        && this.groundDropClaims.Remove(key);

    private PreparedGroundDrop PrepareGroundDrop(DungeonGroundDropClaim claim)
    {
        lock (this.gate)
        {
            var key = new GroundDropClaimKey(claim.RunId, claim.Drop, claim.CharacterId);
            if (!this.groundDropClaims.TryGetValue(key, out var token)
                || token != claim.Token
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || aggregate.CurrentCell != claim.Drop.Cell
                || aggregate.RoomVersion != claim.RoomVersion
                || !aggregate.Cells.TryGetValue(claim.Drop.Cell, out var cell)
                || cell.Revision != claim.CellRevision
                || cell.FindGroundItem(claim.Drop.Number) is null
                || !this.preparedGroundDropClaims.Add(key))
            {
                throw new InvalidOperationException(
                    "The ground-drop reservation changed before its transaction prepared.");
            }

            return new PreparedGroundDrop(key, aggregate);
        }
    }

    private DungeonPickupChange CommitPreparedGroundDrop(
        PreparedGroundDrop prepared,
        DungeonPickupKind kind)
    {
        lock (this.gate)
        {
            // Prepare owns the only reservation for this run/drop. Even if the last participant
            // disconnects after prepare and the aggregate is detached from the directory, this
            // captured aggregate remains the transaction's exact target and commit is infallible.
            _ = this.groundDropClaims.Remove(prepared.Key);
            _ = this.preparedGroundDropClaims.Remove(prepared.Key);
            var cell = prepared.Aggregate.Cells[prepared.Key.Drop.Cell];
            _ = cell.RemoveGroundItem(prepared.Key.Drop.Number)
                ?? throw new InvalidOperationException(
                    "A prepared ground item must still exist when its transaction commits.");
            prepared.Aggregate.RuntimeRevision++;
            return new DungeonPickupChange(
                prepared.Aggregate.RunId,
                prepared.Aggregate.RuntimeRevision,
                prepared.Key.CharacterId,
                prepared.Key.Drop,
                kind,
                [.. prepared.Aggregate.Participants
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)],
                prepared.Aggregate.RoomVersion,
                cell.Revision);
        }
    }

    private void RollbackPreparedGroundDrop(
        DungeonGroundDropClaim claim,
        PreparedGroundDrop? prepared)
    {
        lock (this.gate)
        {
            if (prepared is { } value)
                _ = this.preparedGroundDropClaims.Remove(value.Key);
            _ = this.TryRemoveGroundDropClaim(
                new GroundDropClaimKey(claim.RunId, claim.Drop, claim.CharacterId), claim.Token);
        }
    }

    private void ClearGroundDropClaims(DungeonRunId runId)
    {
        foreach (var key in this.groundDropClaims.Keys
                     .Where(key => key.RunId == runId).ToArray())
        {
            this.groundDropClaims.Remove(key);
        }
    }
    private readonly record struct GroundDropClaimKey(
        DungeonRunId RunId,
        GroundDropId Drop,
        CharacterId CharacterId);
    private readonly record struct GroundDropCommitIdentity(
        DungeonRunDomainService Owner,
        DungeonRunId RunId,
        GroundDropId Drop,
        CharacterId CharacterId,
        long Token);
    private sealed record PreparedGroundDrop(
        GroundDropClaimKey Key,
        DungeonRunAggregate Aggregate);

    private sealed class GroundDropCommitParticipant(
        DungeonRunDomainService owner,
        DungeonGroundDropClaim claim,
        DungeonPickupKind kind) : ITransactionParticipant
    {
        private PreparedGroundDrop? prepared;

        public bool CommitCanFail => false;

        public DungeonPickupChange? Change { get; private set; }

        public DungeonPickupCommit Receipt => new(() => this.Change);

        public void RequireKind(DungeonPickupKind expected)
        {
            if (expected != kind)
                throw new InvalidOperationException(
                    "One ground-drop reservation cannot be committed as two pickup kinds.");
        }

        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.prepared = owner.PrepareGroundDrop(claim);
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            this.Change = owner.CommitPreparedGroundDrop(
                this.prepared ?? throw new InvalidOperationException(
                    "A ground-drop commit must be prepared before it is applied."),
                kind);
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            owner.RollbackPreparedGroundDrop(claim, this.prepared);
            return ValueTask.CompletedTask;
        }
    }
}
