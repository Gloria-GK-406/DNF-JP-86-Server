using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunStore
{
    internal sealed partial class EntryService
    {
        public DungeonEntryChange PreparePartyEntry(
            IReadOnlyList<DungeonRunParticipant> participants,
            DungeonLayout layout,
            DungeonDifficulty difficulty,
            DungeonCellSeed initialCell,
            DungeonRunId runId,
            DateTimeOffset startedAt)
        {
            GameplayWriteAssertion.Writing(nameof(EntryService) + "." + nameof(PreparePartyEntry));
            ArgumentNullException.ThrowIfNull(participants);
            ArgumentNullException.ThrowIfNull(layout);
            ArgumentNullException.ThrowIfNull(initialCell);
            if (!initialCell.IsComplete)
                throw new ArgumentException("A dungeon run cannot publish an incomplete Cell.", nameof(initialCell));
            if (initialCell.Cell != layout.StartCell
                || initialCell.MapId != layout.StartRoomMapId)
            {
                throw new ArgumentException(
                    "The initial Cell identity must match the layout's start room.", nameof(initialCell));
            }
            if (participants.Count == 0)
                throw new ArgumentException("A dungeon run requires at least one participant.", nameof(participants));
            if (participants.Any(participant => participant is null))
                throw new ArgumentException("A dungeon participant cannot be null.", nameof(participants));
            if (participants.Select(participant => participant.CharacterId).Distinct().Count()
                != participants.Count)
            {
                throw new ArgumentException(
                    "A character can occur only once in a dungeon run.", nameof(participants));
            }
            if (participants.Select(participant => participant.Slot).Distinct().Count()
                != participants.Count)
            {
                throw new ArgumentException(
                    "A dungeon participant slot can occur only once.", nameof(participants));
            }
            if (participants.Count(participant => participant.IsLeader) != 1)
            {
                throw new ArgumentException(
                    "A dungeon run requires exactly one leader.", nameof(participants));
            }

            var frozen = participants.ToImmutableArray();
            var entryLeader = frozen.Single(participant => participant.IsLeader);
            var partyKey = this.parties.FindParty(new PartyCharacterSnapshot(
                entryLeader.AccountId, entryLeader.CharacterId, 0, null)).Key;
            var currentParty = this.parties.FindByKey(partyKey)
                ?? throw new InvalidOperationException("The entry party no longer exists.");
            if (!currentParty.Members.Select(member => member.CharacterId)
                    .ToHashSet().SetEquals(frozen.Select(participant => participant.CharacterId)))
                throw new InvalidOperationException("The entry roster no longer matches the party.");
            if (!this.participation.TryClaim(
                frozen.Select(x => x.CharacterId).ToArray(), new(DungeonParticipationMode.Ordinary, runId.Value)))
                throw new InvalidOperationException("A dungeon participant is already occupied.");
            var aggregate = new DungeonRunAggregate(
                runId, partyKey, this.parties, frozen, layout, difficulty, startedAt, initialCell);

            lock (this.store.gate)
            {
                if (this.transactions.Current is { } transaction)
                {
                    var previous = this.store.runsByParty.GetValueOrDefault(partyKey);
                    transaction.Enlist(new object(), () => new EntryCommit(this, aggregate, previous));
                    return aggregate.EntryChange(DungeonEntryChangeKind.Prepared, aggregate.Leader.CharacterId);
                }
                // The party's earlier run ends here without a departure: nobody left, they started over.
                if (this.store.runsByParty.GetValueOrDefault(partyKey) is { } replaced)
                    _ = this.store.EndRun(replaced);

                this.store.Publish(aggregate);
                foreach (var participant in frozen)
                    this.weakness.For(participant.CharacterId).PauseRecovery();

                return aggregate.EntryChange(
                    DungeonEntryChangeKind.Prepared,
                    aggregate.Leader.CharacterId);
            }
        }

        public DungeonEntryChange? TryMarkParticipantReady(
            CharacterId characterId,
            DungeonRunId expectedRunId,
            long expectedRoomVersion)
        {
            GameplayWriteAssertion.Writing(nameof(EntryService) + "." + nameof(TryMarkParticipantReady));
            lock (this.store.gate)
            {
                if (this.store.RunOf(characterId) is not { } aggregate
                    || aggregate.RunId != expectedRunId
                    || aggregate.RoomVersion != expectedRoomVersion
                    || !aggregate.ReadyParticipants.Add(characterId))
                {
                    return null;
                }

                if (aggregate.Layout.Mode is DungeonRunMode.LostLand { Plan: var exploration } && aggregate.LostLandExploration is null)
                    aggregate.LostLandExploration = new(this.clock.GetUtcNow().AddSeconds(exploration.FirstLimitSeconds));
                aggregate.EntryRevision++;
                if (aggregate.Participants.All(member => aggregate.ReadyParticipants.Contains(member.CharacterId)))
                {
                    var cell = aggregate.Cells[aggregate.CurrentCell];
                    cell.Mechanisms = cell.Mechanisms.Select(state => this.mechanismRules.Start(state, this.clock.GetUtcNow())).ToArray();
                    if (cell.IsCleared)
                        cell.Mechanisms = cell.Mechanisms.Select(state => this.mechanismRules.Complete(state, this.clock.GetUtcNow())).ToArray();
                    this.deadlines.Wake(DungeonDeadlineKind.RoomMechanism);
                }
                return aggregate.EntryChange(DungeonEntryChangeKind.ParticipantReady, characterId);
            }
        }

        public DungeonEntryChange? FindEntryChange(CharacterId characterId)
        {
            lock (this.store.gate)
            {
                return this.store.RunOf(characterId) is { } aggregate
                    ? aggregate.EntryChange(DungeonEntryChangeKind.Snapshot, characterId)
                    : null;
            }
        }

        public bool TryAbortEntry(
            DungeonRunId expectedRunId,
            long expectedEntryRevision,
            IReadOnlyList<CharacterId> expectedRecipients)
        {
            GameplayWriteAssertion.Writing(nameof(EntryService) + "." + nameof(TryAbortEntry));
            ArgumentNullException.ThrowIfNull(expectedRecipients);
            lock (this.store.gate)
            {
                if (!this.store.runsById.TryGetValue(expectedRunId, out var aggregate)
                    || aggregate.EntryRevision != expectedEntryRevision
                    || !aggregate.Participants.Select(participant => participant.CharacterId)
                        .ToHashSet().SetEquals(expectedRecipients))
                {
                    return false;
                }

                _ = this.store.EndRun(aggregate);
                return true;
            }
        }
        public DungeonRunState? TryMove(CharacterId characterId, MazeCell target)
        {
            GameplayWriteAssertion.Writing(nameof(EntryService) + "." + nameof(TryMove));
            lock (this.store.gate)
            {
                return this.TryMoveCore(characterId, expectedRunId: null, expectedRoomVersion: null, target);
            }
        }

        public DungeonRunState? TryMove(
            CharacterId characterId,
            DungeonRunId expectedRunId,
            long expectedRoomVersion,
            MazeCell target)
        {
            GameplayWriteAssertion.Writing(nameof(EntryService) + "." + nameof(TryMove));
            lock (this.store.gate)
            {
                return this.TryMoveCore(
                    characterId, expectedRunId, expectedRoomVersion, target, preparedCell: null);
            }
        }

        public DungeonRunState? TryMove(
            CharacterId characterId,
            DungeonRunId expectedRunId,
            long expectedRoomVersion,
            MazeCell target,
            DungeonCellSeed? preparedCell)
        {
            GameplayWriteAssertion.Writing(nameof(EntryService) + "." + nameof(TryMove));
            lock (this.store.gate)
            {
                return this.TryMoveCore(
                    characterId, expectedRunId, expectedRoomVersion, target, preparedCell);
            }
        }

        private DungeonRunState? TryMoveCore(
            CharacterId characterId,
            DungeonRunId? expectedRunId,
            long? expectedRoomVersion,
            MazeCell target,
            DungeonCellSeed? preparedCell = null, bool lostLandItem = false)
        {
            DungeonRunState? Refused(string reason, DungeonRunAggregate? run)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Dungeon move aggregate refused character={CharacterId} run={RunId} expectedRun={ExpectedRunId} version={RoomVersion} expectedVersion={ExpectedVersion} from={FromCell} target={TargetCell} reason={Reason}",
                        characterId, run?.RunId, expectedRunId, run?.RoomVersion, expectedRoomVersion, run?.CurrentCell, target, reason);
                return null;
            }

            if (this.store.RunOf(characterId) is not { } aggregate)
                return Refused("NoRun", null);
            if (aggregate.LostLandExploration?.HasExpired(this.clock.GetUtcNow()) == true)
                return Refused("LostLandEnded", aggregate);
            if (!this.IsCurrentLeader(aggregate, characterId))
                return Refused("NotLeader", aggregate);
            if (expectedRunId is { } runId && aggregate.RunId != runId)
                return Refused("RunChanged", aggregate);
            if (expectedRoomVersion is { } version && aggregate.RoomVersion != version)
                return Refused("RoomVersionChanged", aggregate);
            if (aggregate.Claims.Deaths.Keys.Any(actor => actor.Cell == aggregate.CurrentCell))
                return Refused("ActorDeathPending", aggregate);
            if (aggregate.Claims.GroundDrops.Keys.Any(key => key.Drop.Cell == aggregate.CurrentCell))
                return Refused("GroundDropPending", aggregate);
            if (aggregate.Claims.PreparedGroundDrops.Any(key => key.Drop.Cell == aggregate.CurrentCell))
                return Refused("GroundDropPrepared", aggregate);
            var warp = aggregate.Layout.IsWarp(aggregate.CurrentCell, target);
            if (warp && !aggregate.Cells[aggregate.CurrentCell].IsCleared)
                return Refused("WarpRoomNotCleared", aggregate);
            if (!warp && aggregate.Layout.Mode is not DungeonRunMode.LostLand && Math.Abs(target.X - aggregate.CurrentCell.X)
                    + Math.Abs(target.Y - aggregate.CurrentCell.Y) != 1)
                return Refused("NotAdjacent", aggregate);
            if (lostLandItem && (aggregate.Layout.Mode is not DungeonRunMode.LostLand || !aggregate.Layout.Cells.TryGetValue(target, out var destination)
                || destination.MapCandidates.Count == 0)) return Refused("InvalidLostLandDestination", aggregate);
            if (!lostLandItem && !aggregate.Layout.CanTraverse(aggregate.CurrentCell, target))
                return Refused("Disconnected", aggregate);
            if (!this.sealedTreasureRules.AllowsTraversal(aggregate.Layout.DungeonId,
                    aggregate.KilledMonsterCodes, aggregate.CurrentCell, target))
                return Refused("SealedTreasureGate", aggregate);
            if (aggregate.Cells[aggregate.CurrentCell].MechanismBlocksClear)
                return Refused("MechanismBlocksClear", aggregate);
            if (aggregate.Cells[aggregate.CurrentCell].Mechanisms.Any(state =>
                    !this.mechanismRules.AllowsExit(state, aggregate.CurrentCell, target)))
                return Refused("MechanismBlocksExit", aggregate);
            if (preparedCell is not null
                    && (preparedCell.Cell != target || preparedCell.MapId <= 0
                        || (aggregate.Cells.TryGetValue(target, out var retained) && retained.MapId != preparedCell.MapId)))
                return Refused("PreparedCellMismatch", aggregate);
            if (!aggregate.Cells.ContainsKey(target) && (preparedCell is null || !preparedCell.IsComplete))
                return Refused("IncompleteCell", aggregate);
            if (this.transactions.Current is { } transaction)
            {
                var cell = aggregate.Cells.GetValueOrDefault(target) ?? aggregate.CreateCell(preparedCell!);
                var movement = transaction.Enlist(new object(), () => new MoveCommit(this, aggregate, characterId, target, cell));
                return movement.Preview;
            }
            if (!aggregate.Cells.ContainsKey(target))
            {
                if (preparedCell is null || !preparedCell.IsComplete)
                    return null;
                aggregate.Cells.Add(
                    target,
                    aggregate.CreateCell(preparedCell));
            }
            aggregate.CurrentCell = target;
            aggregate.VisitedCells = aggregate.VisitedCells.Add(target);
            aggregate.RoomVersion++;
            aggregate.EntryRevision++;
            aggregate.ReadyParticipants.Clear();
            this.deadlines.Wake(DungeonDeadlineKind.RoomMechanism);
            return aggregate.ViewFor(characterId);
        }

        private bool IsCurrentLeader(DungeonRunAggregate aggregate, CharacterId characterId)
        {
            return aggregate.Participants.Any(participant =>
                participant.CharacterId == characterId && participant.IsLeader);
        }
        /// <summary>Publishes a prepared run, ending the party's earlier run without a departure.</summary>
        private sealed class EntryCommit(EntryService owner, DungeonRunAggregate next,
            DungeonRunAggregate? previous) : ITransactionParticipant
        {
            public bool CommitCanFail => false;
            public ValueTask PrepareAsync(CancellationToken token)
            {
                lock (owner.store.gate)
                    if (owner.store.runsByParty.GetValueOrDefault(next.PartyKey) != previous)
                        throw new InvalidOperationException("Dungeon entry changed before commit.");
                return ValueTask.CompletedTask;
            }
            public ValueTask CommitAsync(CancellationToken token)
            {
                lock (owner.store.gate)
                {
                    if (previous is not null)
                        _ = owner.store.EndRun(previous);
                    owner.store.Publish(next);
                    foreach (var id in next.EntrantIds)
                    {
                        owner.weakness.For(id).PauseRecovery();
                        owner.creatures.ChangeCombat(id, true);
                    }
                }
                return ValueTask.CompletedTask;
            }
            public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
        }
        private sealed class MoveCommit : ITransactionParticipant
        {
            private readonly EntryService owner;
            private readonly DungeonRunAggregate run;
            private readonly MazeCell target;
            private readonly DungeonCellAggregate cell;
            private readonly long version;
            private readonly CharacterId leader;
            private readonly CharacterId[] members;
            public DungeonRunState Preview { get; }
            public bool CommitCanFail => false;
            public MoveCommit(EntryService owner, DungeonRunAggregate run, CharacterId leader,
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
                lock (owner.store.gate)
                    if (owner.store.RunOf(leader) != run || run.RoomVersion != version
                        || !owner.IsCurrentLeader(run, leader)
                        || !run.ViewFor(leader).Participants.Select(x => x.CharacterId).ToHashSet().SetEquals(members))
                        throw new InvalidOperationException("Dungeon room changed before commit.");
                return ValueTask.CompletedTask;
            }
            public ValueTask CommitAsync(CancellationToken token)
            {
                lock (owner.store.gate)
                {
                    run.Cells.TryAdd(target, cell); run.CurrentCell = target;
                    run.VisitedCells = run.VisitedCells.Add(target); run.RoomVersion++;
                    run.EntryRevision++; run.ReadyParticipants.Clear();
                    owner.deadlines.Wake(DungeonDeadlineKind.RoomMechanism);
                }
                return ValueTask.CompletedTask;
            }
            public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
        }
        public DungeonRunState? TryMoveByLostLandItem(Dfo.Server.Data.Characters.CharacterId character,
            DungeonRunId id, long version, Dfo.Server.Abstractions.Content.MazeCell target, DungeonCellSeed? preparedCell)
        {
            GameplayWriteAssertion.Writing(nameof(EntryService) + "." + nameof(TryMoveByLostLandItem));
            return TryMoveCore(character, id, version, target, preparedCell, lostLandItem: true);
        }
    }
}
