using Dfo.Server.Abstractions.Content;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    private sealed class EntryCommit(DungeonRunDomainService owner, DungeonRunAggregate next,
        Dictionary<CharacterId, DungeonRunAggregate?> previous) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token)
        {
            lock (owner.gate)
                if (previous.Any(x => owner.runsByCharacter.GetValueOrDefault(x.Key) != x.Value))
                    throw new InvalidOperationException("Dungeon entry changed before commit.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken token)
        {
            lock (owner.gate)
            {
                foreach (var id in previous.Keys) owner.DetachParticipant(id);
                owner.runsById.Add(next.RunId, next);
                foreach (var id in previous.Keys)
                {
                    owner.runsByCharacter.Add(id, next);
                    owner.weakness?.For(id).PauseRecovery();
                    owner.creatures?.ChangeCombat(id, true);
                }
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
    private sealed class MoveCommit : ITransactionParticipant
    {
        private readonly DungeonRunDomainService owner;
        private readonly DungeonRunAggregate run;
        private readonly MazeCell target;
        private readonly DungeonCellAggregate cell;
        private readonly long version;
        private readonly CharacterId leader;
        private readonly CharacterId[] members;
        public DungeonRunState Preview { get; }
        public bool CommitCanFail => false;
        public MoveCommit(DungeonRunDomainService owner, DungeonRunAggregate run, CharacterId leader,
            MazeCell target, DungeonCellAggregate cell)
        {
            this.owner = owner; this.run = run; this.target = target; this.cell = cell;
            this.version = run.RoomVersion; this.leader = leader;
            members = run.ViewFor(leader).Participants.Select(x => x.CharacterId).ToArray();
            Preview = run.ViewFor(leader) with {
                CurrentCell = target, CurrentRoomMapId = cell.MapId,
                VisitedCells = run.VisitedCells.Add(target), RoomVersion = version + 1,
                EntryRevision = run.EntryRevision + 1, ReadyParticipants = ImmutableHashSet<CharacterId>.Empty,
                CurrentCellState = cell.Snapshot(), ClearOpen = cell.ClearOpen
            };
        }
        public ValueTask PrepareAsync(CancellationToken token)
        {
            lock (owner.gate)
                if (owner.runsByCharacter.GetValueOrDefault(leader) != run || run.RoomVersion != version
                    || !owner.IsCurrentLeader(run, leader)
                    || !run.ViewFor(leader).Participants.Select(x => x.CharacterId).ToHashSet().SetEquals(members))
                    throw new InvalidOperationException("Dungeon room changed before commit.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken token)
        {
            lock (owner.gate)
            {
                run.Cells.TryAdd(target, cell); run.CurrentCell = target;
                run.VisitedCells = run.VisitedCells.Add(target); run.RoomVersion++;
                run.EntryRevision++; run.ReadyParticipants.Clear();
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
}
