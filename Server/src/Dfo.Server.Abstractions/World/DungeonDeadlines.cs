namespace Dfo.Server.Abstractions.World;

/// <summary>Kinds of in-run deadline served by the dungeon deadline scheduler; one registered source per kind.</summary>
public enum DungeonDeadlineKind { RoomMechanism, Altar, GuardianAltar }

/// <summary>Tells the dungeon deadline scheduler that one kind's deadlines may have changed.</summary>
/// <remarks>
/// Raised by the owner of the deadline state when that state changes, never by command handlers.
/// Wakes coalesce, and the rescan runs as a later executor job, so waking inside a transaction is safe.
/// </remarks>
public interface IDungeonDeadlineWake
{
    void Wake(DungeonDeadlineKind kind);
}
