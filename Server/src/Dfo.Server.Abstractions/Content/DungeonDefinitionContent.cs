using System.Collections.Immutable;

namespace Dfo.Server.Abstractions.Content;

/// <summary>One cell of a maze grid.</summary>
public readonly record struct MazeCell(byte X, byte Y);

/// <summary>How a maze is connected to a quest in dungeon content.</summary>
public enum DungeonQuestConnectionState : byte
{
    /// <summary>The maze is available while the quest is active.</summary>
    Active = 0,

    /// <summary>The maze is available after the quest has been cleared.</summary>
    Cleared = 1,
}

/// <summary>A dungeon-content link between one maze and one quest state.</summary>
/// <param name="MinimumDifficulty">
/// The minimum difficulty declared by the content, or -1 when the connection applies to every
/// difficulty.
/// </param>
public sealed record DungeonQuestConnection(
    DungeonQuestConnectionState State,
    int QuestId,
    int MinimumDifficulty);

/// <summary>
/// One maze a dungeon can be run as: its grid, the rooms on it, and where a run may start and
/// where its boss may stand.
/// </summary>
/// <param name="Index">
/// Position in the dungeon's declared order. This is the number the client is told, so it must
/// stay tied to the archive's order rather than to any sorting we find convenient.
/// </param>
/// <param name="Cells">
/// Occupied cells and their connection/candidate declarations. A cell can exist without an
/// explicit map: its resource then comes from the dungeon's compatible map pool.
/// </param>
/// <param name="StartCandidates">
/// The cells a run may start on. The archive lists candidates rather than one cell, so which is
/// used is a choice the layout makes from its seed rather than a fact of the content.
/// </param>
public sealed record MazeDefinition(
    int Index,
    byte Width,
    byte Height,
    IReadOnlyDictionary<MazeCell, DungeonCellDefinition> Cells,
    IReadOnlyList<MazeCell> StartCandidates,
    IReadOnlyList<MazeCell> BossCandidates,
    DungeonQuestConnection? QuestConnection = null)
{
    /// <summary>A parsed maze's semantic error, checked only if this maze is selected.</summary>
    public string? ConfigurationError { get; init; }
}

[Flags]
public enum DungeonPathDirection : byte
{
    None = 0,
    Right = 1,
    Up = 2,
    Left = 4,
    Down = 8,
    All = Right | Up | Left | Down,
}

/// <summary>Static topology and explicit resource candidates, never a selected runtime map.</summary>
public sealed record DungeonCellDefinition(
    DungeonPathDirection Connections,
    IReadOnlyList<int> MapCandidates,
    IReadOnlyList<int> BossMapCandidates)
{
    /// <summary>False when content omits the door pattern; All then carries no pattern restriction.</summary>
    public bool HasDeclaredConnections { get; init; } = true;

    /// <summary>An explicitly fixed resource with unrestricted connection metadata.</summary>
    public static DungeonCellDefinition Fixed(int mapId) =>
        new(DungeonPathDirection.All, [mapId], []) { HasDeclaredConnections = false };
}

public enum DungeonEntryMode : byte { Maze, Tournament, VillageRevenge }

/// <summary>
/// A dungeon as the archive declares it.
/// </summary>
/// <param name="Mazes">
/// Every maze declared, in archive order. Most dungeons declare one; the ones that declare
/// several expect the server to choose, which is what makes maze choice a rule rather than a
/// lookup.
/// </param>
/// <remarks>
/// Deliberately not carrying a dungeon "type". The archive declares no such thing: only 284 of 839
/// dungeons carry a <c>[dungeon type]</c> tag at all and it marks exception families rather than
/// the ordinary case, so a type enum here would model something that does not exist. What does
/// divide dungeons is structural -- whether they have a maze -- and that shows up as this record
/// being unavailable rather than as a field on it. See Docs/DungeonEntryNotes.md.
/// </remarks>
/// <param name="ExperienceWeight">
/// What this dungeon multiplies a monster's worth by, from its <c>[experience increasing point]</c>.
/// It is how one dungeon pays better than another at the same level and difficulty; the archive
/// gives dungeon 144 a weight of 1.45.
/// </param>
/// <param name="BasisLevel">
/// The dungeon's <c>[basis level]</c>, used when a map requests a relative monster level or
/// declares neither an absolute level nor an adjustment. A map may be shared between dungeons.
/// </param>
/// <param name="OffersPaidCard">
/// Whether a clear of this dungeon offers the paid card, from its <c>[gold card use]</c>.
/// <para>
/// <b>Defaulted to true, and the default is the archive's silence.</b> Five of the 1047 dungeons
/// the list names declare this tag at all -- 100, 3503, 3700, 3901 and 3902 -- and every one of
/// the five declares it as <c>0</c>. The other 1042 say nothing, and the price of a paid flip is
/// declared away from the dungeon entirely, in <c>etc/itemdropinfo_clearreward.etc</c>'s
/// <c>[gold card cost table]</c>, which prices every level. So the ordinary case is that a flip is
/// offered and priced by that table, and a dungeon that declares nothing here is an ordinary case.
/// </para>
/// <para>
/// <b>Which makes <c>0</c> the switch being turned off, and that direction is a reading rather
/// than a declaration.</b> The argument for it is that the other direction is empty: if <c>0</c>
/// meant "offer the paid card" it would restate the default and the five declarations would do
/// nothing at all. No second source settles it -- the reference server parses this tag into a
/// field (<c>86JP</c>: <c>Tool/PvfLib/Models/DungeonFile.cs</c>, defaulting it to <c>-1</c> rather
/// than to either of the two values that occur) and then never reads that field anywhere in its
/// server.
/// </para>
/// <para>
/// <b>What a value other than zero would mean is unascertained</b>, because none occurs: the
/// archive declares this tag five times and writes <c>0</c> five times. Anything else is therefore
/// read as "not turned off" rather than as some third state, which is the reading that cannot
/// invent a behaviour for a value nobody has seen.
/// </para>
/// </param>
public sealed record DungeonDefinition(
    int DungeonId,
    string ArchivePath,
    byte MinimumRequiredLevel,
    bool ConsumesFatigue,
    IReadOnlyList<MazeDefinition> Mazes,
    byte BasisLevel = 1,
    double ExperienceWeight = 1.0,
    bool OffersPaidCard = true,
    DungeonQuestConnection? QuestConnection = null,
    IReadOnlyList<byte>? SupportedDifficulties = null,
    DungeonEntryMode EntryMode = DungeonEntryMode.Maze)
{
    public IReadOnlyList<int> RecommendedLevels { get; init; } = [];
    public bool HasStoryMode { get; init; }
    public bool NoGiveupPenalty { get; init; }
    public ImmutableHashSet<int> NamedMonsterCodes { get; init; } = [];
    public IReadOnlyList<DungeonRequiredItem> RequiredItems { get; init; } = [];
    /// <summary>Preferred alternative to the complete regular requirements group; never additive.</summary>
    public IReadOnlyList<DungeonRequiredItem> EventRequiredItems { get; init; } = [];
    public bool IsSpiciesDungeon { get; init; }
    public int? PartyLimit { get; init; }
    public bool ProhibitsPractice { get; init; }
}

/// <summary>All requirements must be held; only those marked Consume are debited on admission.</summary>
public sealed record DungeonRequiredItem(int ItemId, int Count, bool Consume)
{
    public bool AllowSharedMaterial { get; init; } = true;
}

/// <summary>Reads dungeon declarations out of the content archive.</summary>
public interface IDungeonDefinitionSource
{
    /// <summary>
    /// The dungeon with this identifier, or null when the archive declares none, or declares one
    /// with no maze. A dungeon without a maze is a different structural family -- Tower of Death
    /// is the example -- and answering with an empty maze list would let a caller treat it as an
    /// ordinary dungeon that happens to have nowhere to go.
    /// </summary>
    ValueTask<DungeonDefinition?> GetAsync(int dungeonId, CancellationToken cancellationToken);
}
