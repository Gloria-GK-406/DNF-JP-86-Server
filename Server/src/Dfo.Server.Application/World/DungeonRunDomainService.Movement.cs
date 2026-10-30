using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Logging;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    public DungeonRunState? TryMove(CharacterId characterId, MazeCell target)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryMove));
        lock (this.gate)
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
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryMove));
        lock (this.gate)
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
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryMove));
        lock (this.gate)
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

        if (this.RunOf(characterId) is not { } aggregate)
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
}
