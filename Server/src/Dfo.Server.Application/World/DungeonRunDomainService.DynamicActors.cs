using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    public bool StageDynamicCell(DungeonRunId runId, long roomVersion, long cellRevision,
        IReadOnlyList<DungeonCellActorState> actors, bool mechanismBlocksClear)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(StageDynamicCell));
        var transaction = this.transactions?.Current
            ?? throw new InvalidOperationException("Dynamic cells require a transaction.");
        if (transaction.IsReadOnly) throw new InvalidOperationException("Dynamic cells require a writable transaction.");
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var run) || run.RoomVersion != roomVersion) return false;
            var cell = run.Cells[run.CurrentCell];
            if (cell.Revision != cellRevision || actors.Select(x => x.Sequence).Distinct().Count() != actors.Count
                || actors.Any(x => x.Sequence == 0 || cell.FindActor(x.Sequence) is not null || x.IsDead)) return false;
            var staged = transaction.Enlist((this, runId, roomVersion, "dynamic-cell"),
                () => new DynamicCellCommit(this, run, cell, roomVersion, cellRevision, actors.ToArray(), mechanismBlocksClear));
            return staged.Matches(actors, mechanismBlocksClear, cellRevision);
        }
    }

    private sealed class DynamicCellCommit(DungeonRunDomainService owner, DungeonRunAggregate run,
        DungeonCellAggregate cell, long roomVersion, long revision, IReadOnlyList<DungeonCellActorState> actors,
        bool blocksClear) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public bool Matches(IReadOnlyList<DungeonCellActorState> batch, bool blocked, long expectedRevision) =>
            blocked == blocksClear && expectedRevision == revision && actors.SequenceEqual(batch);
        public ValueTask PrepareAsync(CancellationToken ct)
        {
            lock (owner.gate)
                if (!owner.runsById.TryGetValue(run.RunId, out var current) || current != run
                    || run.RoomVersion != roomVersion || run.Cells[run.CurrentCell] != cell || cell.Revision != revision)
                    throw new InvalidOperationException("Dynamic actor cell changed before commit.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken ct)
        {
            lock (owner.gate)
            {
                cell.AddActors(actors);
                cell.MechanismBlocksClear = blocksClear;
                run.RuntimeRevision++;
                if (!blocksClear && cell.IsCleared && run.Layout.Altar is not null && run.CurrentCell == run.Layout.BossCell)
                    owner.TryOpenClear(run.Participants.First(x => run.IsActiveMember(x.CharacterId)).CharacterId, run.RunId, roomVersion);
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}
