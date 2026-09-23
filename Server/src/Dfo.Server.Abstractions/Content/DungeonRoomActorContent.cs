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
/// <param name="RandomDropCount">Seventh monster integer: number of random drop attempts.</param>
public readonly record struct DungeonRoomActor(
    int Code, byte Level, byte ActorType, int? BasisLevelOffset = null, int RandomDropCount = 1);

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

/// <summary>One generated reward, associated with its original map object index.</summary>
public readonly record struct DungeonRoomDrop(
    byte ObjectIndex, int ItemId, ushort Durability, int Count = 1);

public sealed record ObjectDropEntry(int ItemId, int Weight, ushort Durability);

/// <summary>Static item action. Counts mean independent attempts, not stack quantities.</summary>
public sealed record ObjectDropDefinition(
    byte ObjectIndex, int ObjectCode, int ActionIndex, int LevelOverride,
    int SpecificAttempts, int RandomAttempts, IReadOnlyList<ObjectDropEntry> Entries);

/// <summary>Reads and caches declarations only. Runtime draws belong to the rule service.</summary>
public interface IDungeonRoomDropSource
{
    ValueTask<IReadOnlyList<ObjectDropDefinition>> GetAsync(
        int dungeonId, int mapId, CancellationToken cancellationToken);
}
