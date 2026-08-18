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
public sealed record DungeonLayout(
    int DungeonId,
    int MazeIndex,
    byte Width,
    byte Height,
    MazeCell StartCell,
    MazeCell BossCell,
    int StartRoomMapId,
    int Seed);

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
