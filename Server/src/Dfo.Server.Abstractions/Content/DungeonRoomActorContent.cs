namespace Dfo.Server.Abstractions.Content;

/// <summary>One actor a dungeon room is declared to hold.</summary>
/// <param name="ActorType">
/// What kind of actor the client is to build. Zero through three are monsters; five and up are AI
/// characters, which the room declares separately and which fight beside the player rather than
/// against them.
/// </param>
/// <param name="Level">Absolute level when known; zero requests the dungeon basis level.</param>
/// <param name="BasisLevelOffset">
/// When set, the map requests the dungeon basis level plus this signed adjustment instead of an
/// absolute level. Cleared when the room is materialized so its Cell holds a concrete level.
/// </param>
public readonly record struct DungeonRoomActor(
    int Code, byte Level, byte ActorType, int? BasisLevelOffset = null);

/// <summary>Reads a room's actors out of the map the room resolves to.</summary>
/// <remarks>
/// A room's monsters are the map's, not the dungeon's: <c>.dgn</c> says which map fills each cell
/// and the map says what stands in it. Without this the entry frame reports an empty room, and a
/// client told a room holds nothing has nothing to hit -- the monsters it draws from its own copy
/// of the map are scenery until the server names them.
/// </remarks>
public interface IDungeonRoomActorSource
{
    /// <summary>
    /// The actors this map declares, monsters first and then its AI characters, or an empty list
    /// when the archive declares no such map or the map declares no actors.
    /// </summary>
    ValueTask<IReadOnlyList<DungeonRoomActor>> GetAsync(
        int mapId, CancellationToken cancellationToken);
}

/// <summary>What a room's breakable object is declared to yield.</summary>
/// <param name="ObjectIndex">
/// Which of the room's breakable objects this belongs to, counted in declaration order. The
/// client identifies the object it broke by this number.
/// </param>
/// <param name="Count">
/// How many the object declares. It is the stack count for anything that stacks, and the client
/// reads it off the floor: a material announced without one is drawn with whatever number happened
/// to be in that field, which on 2026-08-29 was a pseudo-random discriminator and read as hundreds
/// of thousands. Equipment does not stack and this stays at one for it.
/// </param>
public readonly record struct DungeonRoomDrop(
    byte ObjectIndex, int ItemId, ushort Durability, int Count = 1);

/// <summary>Reads what a room's breakable objects yield.</summary>
/// <remarks>
/// Two declarations meet here. The room's map names its breakable objects and, for each, which
/// group of the dungeon's reward table it draws from; the dungeon's own file holds those groups.
/// Neither alone says what a room gives.
/// <para>
/// It matters because the tutorial gates on it. Its second room declares one breakable object with
/// an item in it, and a client told the room yields nothing has nothing to break open: the step
/// waits, the scripted pause it opened is never resumed, and the run stops there with the room
/// already cleared. The same dungeon's rooms that declare no such object are unaffected, which is
/// why an empty list is a normal answer rather than a failure.
/// </para>
/// </remarks>
public interface IDungeonRoomDropSource
{
    ValueTask<IReadOnlyList<DungeonRoomDrop>> GetAsync(
        int dungeonId, int mapId, CancellationToken cancellationToken);
}
