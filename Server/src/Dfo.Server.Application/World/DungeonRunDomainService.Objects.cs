using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    public DungeonObjectDestructionClaim? ClaimObjectDestruction(
        CharacterId characterId, DungeonRunId runId, long roomVersion, int objectCode)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(ClaimObjectDestruction));
        var transaction = this.transactions.Current
            ?? throw new InvalidOperationException("Object rewards require an ambient transaction.");
        lock (this.gate)
        {
            if (objectCode <= 0 || this.RunOf(characterId) is not { } run
                || run.RunId != runId || run.RoomVersion != roomVersion)
                return null;
            if (run.LostLandExploration?.HasExpired(this.clock.GetUtcNow()) == true) return null;
            var cell = run.Cells[run.CurrentCell];
            if (!this.sealedTreasureRules.AllowsObjectDestruction(run.Layout.DungeonId, run.KilledMonsterCodes, objectCode))
                return null;
            if (!cell.ObjectQueues.TryGetValue(objectCode, out var queue) || queue.Count == 0
                || cell.ObjectReservations.ContainsKey(objectCode))
                return null;
            var index = queue.Peek();
            var token = Interlocked.Increment(ref this.nextClaim);
            transaction.Enlist((this, runId, cell.Cell, objectCode, token),
                () => new ObjectRewardParticipant(this, run, cell, characterId,
                    roomVersion, objectCode, index, token));
            cell.ObjectReservations.Add(objectCode, token);
            return new(objectCode, index,
                cell.ObjectAt(index).PendingLoot.ToArray());
        }
    }

    private sealed class ObjectRewardParticipant(
        DungeonRunDomainService owner, DungeonRunAggregate run, DungeonCellAggregate cell,
        CharacterId characterId, long roomVersion, int code, int index, long token)
        : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (owner.gate)
            {
                if (!owner.runsById.TryGetValue(run.RunId, out var active)
                    || !ReferenceEquals(active, run) || !run.IsMember(characterId)
                    || run.CurrentCell != cell.Cell || run.RoomVersion != roomVersion
                    || cell.ObjectReservations.GetValueOrDefault(code) != token
                    || cell.ObjectQueues[code].Peek() != index)
                    throw new InvalidOperationException("The object reward reservation is no longer current.");
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            lock (owner.gate)
            {
                // The captured Cell remains the target even if its run left the live directory
                // after prepare. No fallible work follows the durable inventory commit.
                cell.ConsumeObject(index);
                cell.ObjectQueues[code].Dequeue();
                cell.ObjectReservations.Remove(code);
                run.RuntimeRevision++;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            lock (owner.gate)
            {
                if (cell.ObjectReservations.GetValueOrDefault(code) == token)
                    cell.ObjectReservations.Remove(code);
            }
            return ValueTask.CompletedTask;
        }
    }
}
