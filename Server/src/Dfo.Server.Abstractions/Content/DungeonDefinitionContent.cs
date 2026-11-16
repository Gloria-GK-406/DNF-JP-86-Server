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

/// <summary>The kind of one <c>[clear condition]</c> entry, numbered as A21 and 70 both number it.</summary>
/// <remarks>
/// A21 client 0x02AA0886..0x02AA09AF and 70 <c>ImportDungeonScript</c> @ 0x088E8764 agree on 0..4;
/// <see cref="LimitTime"/> is A21-only and has no evidenced meaning, so it is read and not run.
/// </remarks>
public enum DungeonClearConditionKind : byte
{
    DestroyObject = 0,
    ClearMap = 1,
    HuntMonster = 2,
    HuntApc = 3,
    HuntBoss = 4,
    LimitTime = 5,
}

/// <summary>One entry of a maze's <c>[clear condition]</c>.</summary>
/// <param name="Targets">
/// The object, map or monster codes the entry counts. More than one only for the
/// <c>list N a b ...</c> form, which any one of them satisfies.
/// </param>
/// <param name="Count">How many times the targets must be met; a clear map entry is always 1.</param>
public sealed record DungeonClearCondition(DungeonClearConditionKind Kind, IReadOnlyList<int> Targets, int Count)
{
    /// <summary>Whether the server counts this entry. Limit time has no evidenced rule.</summary>
    public bool IsCounted => this.Kind != DungeonClearConditionKind.LimitTime;
}

/// <summary>One <c>[hunt monster]</c> target of a <c>[boss room entrance condition]</c>.</summary>
/// <param name="Code">The conditional monster to hunt; one entry is one placed instance.</param>
/// <param name="First">Second integer of the triple, kept as declared (2010: 1, 2016: 1, 2005: 0).</param>
/// <param name="Second">Third integer of the triple, kept as declared (2010: 1, 2016: 4, 2005: 2).</param>
public sealed record DungeonEntranceHuntTarget(int Code, int First, int Second);

/// <summary>
/// <c>[boss room entrance condition]</c>: conditional monsters spread over the maze and what happens
/// once all are hunted. Only the summon action is run; any other action (2005's
/// <c>[time condition]</c>) is kept as <see cref="UnsupportedAction"/> and the whole condition is
/// left unexecuted.
/// </summary>
/// <param name="SkipsBossRoomCheck"><c>[no check enter boss room]</c>.</param>
/// <param name="SummonCode">The monster <c>[clear action] [summon monster] N code</c> calls in.</param>
/// <param name="SummonCount">The N of the summon action.</param>
public sealed record DungeonBossRoomEntranceCondition(
    bool SkipsBossRoomCheck,
    int SummonCode,
    int SummonCount,
    IReadOnlyList<DungeonEntranceHuntTarget> Hunts)
{
    public string? UnsupportedAction { get; init; }
    public bool IsExecutable => this.UnsupportedAction is null && this.SummonCode > 0 && this.Hunts.Count > 0;
}

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

    /// <summary>
    /// <c>[clear condition]</c> blocks of this maze, one entry per block. When any is counted, the
    /// maze clears on them alone and a cleared boss room no longer counts (70
    /// <c>IsClearDungeon</c> @ 0x0822D08C).
    /// </summary>
    public IReadOnlyList<DungeonClearCondition> ClearConditions { get; init; } = [];
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
    public LinkedDungeonDefinition? LinkedDungeon { get; init; }
    /// <summary>When declared, charge this amount only on entry instead of per new room.</summary>
    public ushort? EntryOnlyFatigue { get; init; }
    /// <summary>
    /// <c>[champion]</c>: one value per difficulty (0..4), the base of the chance that a room gains
    /// one random champion. Empty when the file does not declare it, which 70 reads as zero.
    /// </summary>
    public IReadOnlyList<int> ChampionRates { get; init; } = [];
    /// <summary>
    /// <c>[ignore default dungeon clear]</c>: a cleared boss room does not clear this dungeon. The
    /// A21 client only stores the flag (0x02AB8810 -> +0xA10) and never reads it in plain code,
    /// so its meaning is the server's.
    /// </summary>
    public bool IgnoresDefaultClear { get; init; }
    /// <summary><c>[boss room entrance condition]</c>, when the dungeon declares one.</summary>
    public DungeonBossRoomEntranceCondition? BossRoomEntrance { get; init; }
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
