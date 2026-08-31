using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// One actor of a room, with what its declared type means already read.
/// </summary>
/// <remarks>
/// The flag travels with the actor because the frame that draws the room needs it per actor and has
/// no business knowing where the boundary between a monster and an ally sits. Which type is which
/// is a fact about the game, and the rules own it.
/// </remarks>
public readonly record struct RoomActorDescription(DungeonRoomActor Actor, bool IsAiCharacter);

/// <summary>
/// What the room a run is standing in holds, as the room is about to be described.
/// </summary>
/// <param name="GroundDrops">
/// What the room still declares and nobody has taken, each under the number the client names it by.
/// </param>
/// <param name="FirstActorSequence">
/// The running number the room's first actor is given. Every later actor takes the next one.
/// </param>
public sealed record DungeonRoomContents(
    IReadOnlyList<RoomActorDescription> Actors,
    IReadOnlyList<GroundDrop> GroundDrops,
    ushort FirstActorSequence);

/// <summary>
/// Which of a room's declared actors a running number names, and how many the room declares.
/// </summary>
/// <param name="Actor">
/// Null when the number names none of them, which is the two sides disagreeing about what is in the
/// room rather than an error.
/// </param>
/// <param name="DeclaredCount">
/// Carried alongside because a caller that has to say why it refused needs to say how many there
/// were.
/// </param>
public readonly record struct DeclaredActorLookup(DungeonRoomActor? Actor, int DeclaredCount);

/// <summary>
/// What a dungeon room declares, and what a run standing in it makes of that declaration.
/// </summary>
/// <remarks>
/// A room's contents are content plus one fact: the archive says what the map holds, and the run
/// says which of it is still there and what numbers it goes under. Both halves are here so that the
/// numbering, the monster-or-ally boundary and the clearing test are answered in one place instead
/// of being re-derived by each frame that needs them.
/// <para>
/// The run is handed in. Nothing here looks a run up, so this reads no world state.
/// </para>
/// </remarks>
public interface IDungeonRoomRuleService
{
    /// <summary>
    /// What to describe the run's current room as holding.
    /// </summary>
    /// <param name="firstEntry">
    /// False when the character has stood in this room before, in which case the room is described
    /// as empty: the client already killed what was in there and still holds those identities, and
    /// sending the list again resurrects them under the very same numbers.
    /// </param>
    ValueTask<DungeonRoomContents> DescribeAsync(
        DungeonRunState run, bool firstEntry, CancellationToken cancellationToken);

    /// <summary>Which declared actor this running number names.</summary>
    ValueTask<DeclaredActorLookup> FindDeclaredActorAsync(
        DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken);

    /// <summary>Whether every actor the run's current room declares has been reported dead.</summary>
    ValueTask<bool> IsCurrentRoomClearedAsync(
        DungeonRunState run, CancellationToken cancellationToken);

    /// <summary>What the run's current room still has on its floor, under its numbers.</summary>
    ValueTask<IReadOnlyList<GroundDrop>> RemainingGroundDropsAsync(
        DungeonRunState run, CancellationToken cancellationToken);

    /// <summary>What this room's breakable objects are declared to yield.</summary>
    ValueTask<IReadOnlyList<DungeonRoomDrop>> DeclaredDropsAsync(
        int dungeonId, int mapId, CancellationToken cancellationToken);

    /// <summary>
    /// What each of these declared actors is, as the frame that draws the room needs it.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="DescribeAsync"/> so that the boundary between a monster and an ally
    /// can be asked for on its own, by a caller that already holds the actors.
    /// </remarks>
    IReadOnlyList<RoomActorDescription> Describe(IReadOnlyList<DungeonRoomActor> declared);

    /// <summary>The number the run's current room gives its first actor.</summary>
    ushort FirstActorSequenceFor(DungeonRunState run);
}
