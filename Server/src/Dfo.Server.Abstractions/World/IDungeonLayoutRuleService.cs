using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// What a caller knows about the character asking to enter, gathered before the rule is consulted.
/// </summary>
/// <remarks>
/// Passed in rather than looked up. A rule that queried the world would become a second owner of
/// it, and it would stop being replayable: the same seed would produce different layouts as the
/// world moved underneath it. Collecting these is the caller's job precisely because the caller is
/// the one holding the lease while it does so.
/// </remarks>
public sealed record DungeonEntryFacts(
    byte Difficulty,
    IReadOnlyCollection<int> ActiveQuestIds,
    IReadOnlyCollection<int> ClearedQuestIds)
{
    public static DungeonEntryFacts None { get; } = new(0, [], []);
}

/// <summary>
/// One dungeon run's maze, fixed at the moment of entry.
/// </summary>
/// <param name="Seed">
/// The seed the layout was derived from. Carried rather than discarded because it is not an
/// internal detail: NOTI 0x001D START_MAP puts it on the wire at offset 3, and the client draws
/// on it, so the server and the client must agree on the value.
/// </param>
/// <remarks>
/// A value, not an instance. It has no identity and no lifetime: the same dungeon, facts and seed
/// always produce an equal layout, which is what makes a run reproducible from its record. The
/// object with identity is the dungeon instance a domain service builds from this.
/// <para>
/// It must then be kept rather than recomputed. Maze choice depends on quest state as it stood at
/// entry, and quest state moves; recomputing later would answer with a different maze than the one
/// the client was told about.
/// </para>
/// </remarks>
/// <param name="Rooms">
/// Every cell that carries a room, and the map each resolves to. Carried here rather than looked
/// up again when a character walks between rooms: the maze was chosen from quest state as it stood
/// at entry, and reading the archive again later would answer for whichever maze the rule would
/// pick now.
/// </param>
/// <param name="ExperienceWeight">
/// What this dungeon multiplies a monster's worth by. Carried here for the same reason the basis
/// level is: a run knows its layout, and a kill in it needs the dungeon's own numbers without
/// going back to the archive. It is not a bonus dungeons may only add -- the tutorial's is 0.1,
/// and it pays a tenth.
/// </param>
public sealed record DungeonLayout(
    int DungeonId,
    int MazeIndex,
    byte Width,
    byte Height,
    MazeCell StartCell,
    MazeCell BossCell,
    int StartRoomMapId,
    int Seed,
    IReadOnlyDictionary<MazeCell, int> Rooms,
    byte BasisLevel = 1,
    double ExperienceWeight = 1.0)
{

    /// <summary>What a monster of this run is worth when its map declines to say.</summary>
    /// <remarks>
    /// The dungeon's <c>[basis level]</c>, carried here because a map is shared between dungeons
    /// and cannot hold it. Most maps declare monster level zero and lean on this entirely; reading
    /// a declared zero as a level rather than as an absence made every such monster level one.
    /// </remarks>
    public byte LevelOf(byte declaredLevel) => declaredLevel > 0 ? declaredLevel : BasisLevel;

    /// <summary>What this cell's room is for.</summary>
    public DungeonRoomKind KindOf(MazeCell cell) =>
        cell == BossCell ? DungeonRoomKind.Boss
        : cell == StartCell ? DungeonRoomKind.Start
        : DungeonRoomKind.Ordinary;
}

/// <summary>
/// What a room is for, which is what decides what may be done in it.
/// </summary>
/// <remarks>
/// A kind rather than a phase. Whether a player may walk back out of a room is a property of the
/// room, not of how far the run has got: the boss room's doors shut behind you whether or not the
/// boss is dead, and the rooms a dungeon may add later -- a shop, a puzzle, a checkpoint -- each
/// bring their own answer with them. Asking the kind leaves room for those; asking the run's phase
/// would have to be rewritten for every one of them.
/// </remarks>
public enum DungeonRoomKind : byte
{
    /// <summary>An ordinary room: walk in, walk out.</summary>
    Ordinary = 0,

    /// <summary>Where the run opens.</summary>
    Start,

    /// <summary>The room the run ends in. There is no way back out of it.</summary>
    Boss,
}

/// <summary>Turns dungeon content, entry facts and an explicit seed into a run's maze.</summary>
public interface IDungeonLayoutRuleService
{
    /// <summary>
    /// The layout for this run, or null when the archive declares no enterable dungeon under this
    /// identifier.
    /// </summary>
    /// <param name="seed">
    /// Supplied by the caller and never invented here. A rule that reached for ambient randomness
    /// could not be tested against a fixed expectation, and the same run could not be replayed
    /// from its record.
    /// </param>
    ValueTask<DungeonLayout?> ResolveAsync(
        int dungeonId,
        DungeonEntryFacts facts,
        int seed,
        CancellationToken cancellationToken);
}
