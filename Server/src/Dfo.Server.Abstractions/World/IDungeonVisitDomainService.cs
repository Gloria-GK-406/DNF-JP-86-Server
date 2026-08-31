using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// One piece still lying on a room's floor, with what kind it is already read.
/// </summary>
/// <remarks>
/// The flag travels with the piece for the same reason an actor's does: the frame that announces the
/// floor needs it per piece, and which kind counts as equipment is a fact about items that a frame
/// has no business holding.
/// </remarks>
public readonly record struct FloorDropDescription(GroundDrop Drop, bool IsEquipment);

/// <summary>
/// What a character finds on walking into a room.
/// </summary>
/// <param name="FirstEntry">
/// False when the character has stood in this room before, in which case it is described as empty.
/// </param>
public sealed record DungeonRoomVisit(
    DungeonRunState Run,
    bool FirstEntry,
    IReadOnlyList<RoomActorDescription> Actors,
    IReadOnlyList<FloorDropDescription> FloorDrops,
    ushort FirstActorSequence);

/// <summary>A dungeon entered: the layout it was given, and its first room.</summary>
public sealed record DungeonEntry(DungeonLayout Layout, DungeonRoomVisit Visit);

/// <summary>
/// Entering a dungeon and walking between its rooms.
/// </summary>
/// <remarks>
/// The use case behind two frames, and the reason it is a service rather than two handlers: entering
/// resolves a layout from quest state, remembers a run against it, and describes the room the
/// character lands in — three things that have to happen together and in that order, because the
/// layout cannot be recomputed later and still describe the run the client is in.
/// <para>
/// It orchestrates: the run is world state and lives in <see cref="IDungeonRunDomainService"/>,
/// while what a dungeon's maze is, what a room holds and what kind a piece is are rules. Nothing
/// about the wire is decided here; a caller is handed values and builds its own frames.
/// </para>
/// </remarks>
public interface IDungeonVisitDomainService
{
    /// <summary>
    /// Whether the selection screen's state is deferred to this character's first entry.
    /// </summary>
    /// <remarks>
    /// Asked by two frames built in two slices -- the screen's own burst and the answer to
    /// entering -- which is why it is here rather than in either of them.
    /// </remarks>
    bool DefersSelectionState(byte characterLevel);

    /// <summary>
    /// Begins a run and describes its first room, or null when the archive declares no enterable
    /// dungeon under that identifier.
    /// </summary>
    /// <remarks>
    /// The facts are handed in rather than found: which quests a character carries comes from the
    /// entry batch the session was published with, so that the layout is chosen against the state
    /// the client was told about rather than a fresher read of it.
    /// </remarks>
    ValueTask<DungeonEntry?> EnterAsync(
        CharacterId characterId,
        int dungeonId,
        byte difficulty,
        DungeonEntryFacts facts,
        CancellationToken cancellationToken);

    /// <summary>
    /// Walks the character into an adjoining cell and describes what is there, or null when the
    /// maze declares no room at that cell.
    /// </summary>
    /// <param name="current">
    /// The run as the caller found it. Handed in rather than looked up again, so that whether this
    /// is a first entry is decided against the same run the caller checked the room's exits on.
    /// </param>
    ValueTask<DungeonRoomVisit?> MoveAsync(
        CharacterId characterId,
        DungeonRunState current,
        MazeCell target,
        CancellationToken cancellationToken);
}
