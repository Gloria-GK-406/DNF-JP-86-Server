using System.Collections.Immutable;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

public enum DungeonEntryIntentKind : byte
{
    Interactive = 0,
    Directed = 1,
    Cycle = 2,
    OrdinaryRechallenge = 3,
}

/// <summary>The client-side selection path that immediately precedes dungeon entry.</summary>
public readonly record struct DungeonEntryIntent(
    DungeonEntryIntentKind Kind,
    int? DirectedDungeonId,
    int? CycleQuestId = null)
{
    public static DungeonEntryIntent Interactive { get; } =
        new(DungeonEntryIntentKind.Interactive, null);

    public static DungeonEntryIntent Directed(int dungeonId) =>
        dungeonId > 0
            ? new DungeonEntryIntent(DungeonEntryIntentKind.Directed, dungeonId)
            : throw new ArgumentOutOfRangeException(nameof(dungeonId));

    public static DungeonEntryIntent Cycle(int dungeonId, int questId) =>
        dungeonId > 0 && questId > 0
            ? new(DungeonEntryIntentKind.Cycle, dungeonId, questId)
            : throw new ArgumentOutOfRangeException(nameof(dungeonId));

    public static DungeonEntryIntent OrdinaryRechallenge(int dungeonId) =>
        dungeonId > 0
            ? new(DungeonEntryIntentKind.OrdinaryRechallenge, dungeonId)
            : throw new ArgumentOutOfRangeException(nameof(dungeonId));
}

public enum DungeonRunPurposeKind : byte
{
    Ordinary = 0,
    QuestLinked = 1,
    Cycle = 2,
}

/// <summary>The reason this particular run selected its maze, frozen at entry.</summary>
public readonly record struct DungeonRunPurpose(
    DungeonRunPurposeKind Kind,
    int? QuestId,
    DungeonQuestConnectionState? QuestState)
{
    public static DungeonRunPurpose Ordinary { get; } =
        new(DungeonRunPurposeKind.Ordinary, null, null);

    public static DungeonRunPurpose QuestLinked(
        int questId,
        DungeonQuestConnectionState state) =>
        questId > 0
            ? new DungeonRunPurpose(DungeonRunPurposeKind.QuestLinked, questId, state)
            : throw new ArgumentOutOfRangeException(nameof(questId));

    public bool IsQuestLinked => Kind is DungeonRunPurposeKind.QuestLinked or DungeonRunPurposeKind.Cycle;
}

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
    DungeonDifficulty Difficulty,
    IReadOnlyCollection<int> ActiveQuestIds,
    IReadOnlyCollection<int> ClearedQuestIds,
    DungeonEntryIntent Intent = default)
{
    public static DungeonEntryFacts None { get; } = new(0, [], [], DungeonEntryIntent.Interactive);
    /// <summary>Specialized rules may constrain maze choice without duplicating topology validation.</summary>
    public int? RequiredMazeIndex { get; init; }
}

public sealed record DungeonObjectSpawn(MazeCell Cell, int X, int Y, int ObjectId, int Team, int SpawnMode, bool Regenerate);

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
/// <param name="Cells">
/// The frozen maze topology and resource candidate declarations. Selected maps belong to the
/// run's cell instances; entering another room never reselects the maze from current quest state.
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
    IReadOnlyDictionary<MazeCell, DungeonCellDefinition> Cells,
    byte BasisLevel = 1,
    double ExperienceWeight = 1.0,
    DungeonRunPurpose Purpose = default,
    bool ConsumesFatigue = true)
{
    /// <summary>The run's gameplay kind and the plan that kind froze at entry.</summary>
    public DungeonRunMode Mode { get; init; } = DungeonRunMode.Ordinary;
    public ushort? EntryOnlyFatigue { get; init; }
    // 70 CParty::giveup_game exempts every nonzero blood-dungeon type from stamina penalties.
    public bool NoGiveupPenalty { get; init; }
    public bool AppliesFailureWeakness => Mode.Capabilities.AppliesFailureWeakness && !NoGiveupPenalty;
    /// <summary>
    /// Entry-frozen level for ordinary monsters. Null preserves MAP declarations;
    /// APCs, dummy actors and special summon templates keep their own level rules.
    /// Applied once by room rules, before the Cell freezes actors and loot.
    /// </summary>
    public byte? FixedMonsterLevel { get; init; }
    public ImmutableHashSet<int> NamedMonsterCodes { get; init; } = [];
    /// <summary>Rule-declared combatants whose script dummy flag must not make a room skippable.</summary>
    public ImmutableHashSet<int> RequiredCombatantCodes { get; init; } = [];
    public ImmutableArray<DungeonObjectSpawn> RandomObjects { get; init; } = [];
    public ImmutableDictionary<MazeCell, MazeCell> Warps { get; init; } = ImmutableDictionary<MazeCell, MazeCell>.Empty;

    public bool IsWarp(MazeCell from, MazeCell target) => Warps.TryGetValue(from, out var destination) && destination == target;

    /// <summary>Both cells must expose the matching door, not just be adjacent on the grid.</summary>
    public bool CanTraverse(MazeCell from, MazeCell target)
    {
        if (Mode is DungeonRunMode.LostLand { Plan: var exploration })
        {
            if (from == target || !Cells.TryGetValue(from, out var origin) || origin.MapCandidates.Count == 0
                || !Cells.TryGetValue(target, out var room) || room.MapCandidates.Count == 0) return false;
            if (exploration.IsDeclaredWarp(from, target)) return true;
        }
        if (IsWarp(from, target)) return Cells.ContainsKey(from) && Cells.ContainsKey(target);
        var entrance = EntranceFrom(from, target);
        return entrance != DungeonPathDirection.None
            && Cells.TryGetValue(from, out var source)
            && Cells.TryGetValue(target, out var destination)
            && (destination.Connections & entrance) != 0
            && (source.Connections & Opposite(entrance)) != 0;
    }

    public static DungeonPathDirection EntranceFrom(MazeCell from, MazeCell target) =>
        ((int)target.X - from.X, (int)target.Y - from.Y) switch
        {
            (1, 0) => DungeonPathDirection.Left,
            (-1, 0) => DungeonPathDirection.Right,
            (0, 1) => DungeonPathDirection.Up,
            (0, -1) => DungeonPathDirection.Down,
            _ => DungeonPathDirection.None,
        };

    private static DungeonPathDirection Opposite(DungeonPathDirection direction) => direction switch
    {
        DungeonPathDirection.Left => DungeonPathDirection.Right,
        DungeonPathDirection.Right => DungeonPathDirection.Left,
        DungeonPathDirection.Up => DungeonPathDirection.Down,
        DungeonPathDirection.Down => DungeonPathDirection.Up,
        _ => DungeonPathDirection.None,
    };

    /// <summary>Resolve an absolute map level or a level relative to this dungeon.</summary>
    /// <remarks>
    /// The dungeon's <c>[basis level]</c> belongs here because a map may be shared between
    /// dungeons. A relative map declaration carries a signed adjustment; an absent absolute level
    /// falls back to the basis level.
    /// </remarks>
    public byte LevelOf(byte declaredLevel, int? basisLevelOffset = null)
    {
        if (basisLevelOffset is { } offset)
        {
            var adjusted = (long)BasisLevel + offset;
            return adjusted > 0 ? (byte)Math.Min(adjusted, byte.MaxValue) : BasisLevel;
        }

        return declaredLevel > 0 ? declaredLevel : BasisLevel;
    }

    /// <summary>What this cell's room is for.</summary>
    public DungeonRoomKind KindOf(MazeCell cell) =>
        Mode is DungeonRunMode.LostLand ? DungeonRoomKind.Exploration
        : cell == BossCell ? DungeonRoomKind.Boss
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
    /// <summary>A timed exploration room; no ordinary boss settlement.</summary>
    Exploration,
}

/// <summary>Turns dungeon content, entry facts and an explicit seed into a run's maze.</summary>
public interface IDungeonStructureRuleService
{
    ValueTask<DungeonDefinition?> GetDefinitionAsync(int dungeonId, CancellationToken cancellationToken);

    /// <summary>Select and validate the maze before mode-specific room replacements are applied.</summary>
    ValueTask<DungeonLayout?> ResolveTopologyAsync(int dungeonId, DungeonEntryFacts facts, int seed,
        CancellationToken cancellationToken) => ResolveAsync(dungeonId, facts, seed, cancellationToken);

    /// <summary>Select the actual start resource and validate the final run's reachable room candidates.</summary>
    ValueTask<DungeonLayout> PrepareLayoutAsync(DungeonLayout layout, CancellationToken cancellationToken) =>
        ValueTask.FromResult(layout);

    /// <summary>Choose on first entry only. The domain instance retains the selected map on revisit.</summary>
    ValueTask<DungeonMapSelection> SelectMapAsync(
        DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken cancellationToken);
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

public enum DungeonMapSelectionFailure { CellNotFound, Disconnected, NoCompatibleMap }

public readonly record struct DungeonMapSelection(int? MapId, DungeonMapSelectionFailure? Failure)
{
    public static DungeonMapSelection Selected(int mapId) => new(mapId, null);
    public static DungeonMapSelection Refused(DungeonMapSelectionFailure failure) => new(null, failure);
}
