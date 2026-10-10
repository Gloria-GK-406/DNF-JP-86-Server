using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public enum DungeonRoomMechanismKind { Elevator }
public enum DungeonRoomMechanismOutcome : byte { Running, Normal, Crash }

/// <summary>Rules frozen at Cell creation, independent of the dungeon's numeric identity.</summary>
public sealed record DungeonRoomMechanismDefinition(DungeonRoomMechanismKind Kind,
    TimeSpan StageDuration, byte LastStage);

/// <summary>One retained room-owned mechanism; revisiting never restarts its clock.</summary>
public sealed record DungeonRoomMechanismState(DungeonRoomMechanismDefinition Definition,
    DateTimeOffset? StartedAt = null, byte Stage = 0,
    DungeonRoomMechanismOutcome Outcome = DungeonRoomMechanismOutcome.Running, long Revision = 0);

public sealed record DungeonRoomMechanismView(DungeonRunId RunId, MazeCell Cell, long RoomVersion,
    IReadOnlyList<DungeonRoomMechanismState> States, IReadOnlyList<CharacterId> Recipients);

public sealed record DungeonRoomMechanismsChanged(DungeonRoomMechanismView View) : IDomainEvent;
public sealed record DungeonRoomMechanismsNotification(DungeonRoomMechanismView View)
    : INotification<DungeonRoomMechanismsNotification>;

public interface IDungeonRoomMechanismRuleService
{
    DungeonRoomMechanismDefinition Define(DungeonRoomMechanismKind kind);
    DungeonRoomMechanismState Start(DungeonRoomMechanismState state, DateTimeOffset now);
    DungeonRoomMechanismState Advance(DungeonRoomMechanismState state, DateTimeOffset now);
    DungeonRoomMechanismState Complete(DungeonRoomMechanismState state, DateTimeOffset occurredAt);
    DateTimeOffset? NextDeadline(DungeonRoomMechanismState state);
    bool AllowsExit(DungeonRoomMechanismState state, MazeCell from, MazeCell to);
}

/// <summary>Coalesced wakeups for one asynchronous deadline worker, not a per-room thread.</summary>
public interface IDungeonRoomMechanismWakeSignal
{
    void Wake();
    ValueTask WaitAsync(DateTimeOffset? deadline, CancellationToken cancellationToken);
}
