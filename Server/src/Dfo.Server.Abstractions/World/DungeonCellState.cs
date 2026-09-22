using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>Why one item is on a dungeon cell's floor.</summary>
public enum DungeonGroundItemSourceKind
{
    DeclaredObject,
    ActorDeath,
    PlayerDrop,
}

/// <summary>The stable protocol identity carried by one cell-owned ground item.</summary>
public readonly record struct DungeonGroundItemSource(
    DungeonGroundItemSourceKind Kind,
    byte? ObjectIndex = null,
    ushort? ActorSequence = null)
{
    public static DungeonGroundItemSource Declared(byte objectIndex) =>
        new(DungeonGroundItemSourceKind.DeclaredObject, ObjectIndex: objectIndex);

    public static DungeonGroundItemSource FromActor(ushort actorSequence) =>
        new(DungeonGroundItemSourceKind.ActorDeath, ActorSequence: actorSequence);

    public static DungeonGroundItemSource FromPlayer() =>
        new(DungeonGroundItemSourceKind.PlayerDrop);
}

/// <summary>One item currently owned by a dungeon cell's floor.</summary>
public sealed record DungeonGroundItem(
    ushort Number,
    int ItemId,
    int Count,
    ushort Durability,
    bool IsEquipment,
    DungeonGroundItemSource Source,
    DropRarity Rarity = DropRarity.Common,
    DropItemKind Kind = DropItemKind.Stackable,
    CharacterId? OwnerCharacterId = null)
{
    public bool IsGold => this.ItemId == RolledDrop.GoldItemId;

    public GroundPiece ToGroundPiece() => new(
        this.ItemId,
        Math.Max(1, this.Count),
        this.Durability,
        this.IsEquipment,
        this.Source.ObjectIndex,
        this.Source.ActorSequence,
        this.OwnerCharacterId);

    public MonsterDrop ToMonsterDrop() => new(
        this.Number,
        new RolledDrop(
            this.ItemId,
            this.Count,
            this.Rarity,
            this.Kind,
            this.Durability,
            this.OwnerCharacterId));
}

/// <summary>One actor and the runtime facts that belong to its cell.</summary>
public sealed record DungeonCellActorState(
    ushort Sequence,
    DungeonRoomActor Actor,
    bool IsAiCharacter,
    bool IsDead,
    IReadOnlyList<RolledDrop> PendingLoot,
    bool IsMonster = true);

/// <summary>An immutable mirror of one fully-created dungeon cell.</summary>
public sealed record DungeonCellState(
    MazeCell Cell,
    int MapId,
    long Revision,
    ushort FirstActorSequence,
    IReadOnlyList<DungeonCellActorState> Actors,
    IReadOnlyList<DungeonGroundItem> GroundItems,
    bool ClearOpen = false)
{
    public bool IsCleared => this.Actors
        .Where(actor => actor.IsMonster)
        .All(actor => actor.IsDead);

    public DungeonCellActorState? FindActor(ushort sequence) =>
        this.Actors.FirstOrDefault(actor => actor.Sequence == sequence);

    public DungeonGroundItem? FindGroundItem(ushort number) =>
        this.GroundItems.FirstOrDefault(item => item.Number == number);
}

/// <summary>
/// A complete, unpublished cell prepared from content and rules before a run makes it visible.
/// Ground-item numbers are provisional; the run assigns their shared wire identities on adoption.
/// </summary>
public sealed record DungeonCellSeed(
    MazeCell Cell,
    int MapId,
    ushort FirstActorSequence,
    IReadOnlyList<DungeonCellActorState> Actors,
    IReadOnlyList<DungeonGroundItem> GroundItems,
    bool IsComplete = true)
{
    public DungeonCellState Snapshot() => new(
        this.Cell,
        this.MapId,
        Revision: 0,
        this.FirstActorSequence,
        this.Actors,
        this.GroundItems);

    public static DungeonCellSeed CompleteEmpty(MazeCell cell, int mapId) =>
        new(cell, mapId, FirstActorSequence: 1, [], []);
}

/// <summary>Source-neutral input for adding one item to an already-published Cell.</summary>
public sealed record DungeonGroundItemDraft(
    int ItemId,
    int Count,
    ushort Durability,
    bool IsEquipment,
    DungeonGroundItemSource Source,
    DropRarity Rarity = DropRarity.Common,
    DropItemKind Kind = DropItemKind.Stackable,
    CharacterId? OwnerCharacterId = null);

/// <summary>One ordered addition to a published Cell's unified ground-item collection.</summary>
public sealed record DungeonGroundItemsAddedChange(
    DungeonRunState Run,
    long Revision,
    long CellRevision,
    IReadOnlyList<DungeonGroundItem> AddedGroundItems,
    IReadOnlyList<CharacterId> Recipients);

/// <summary>Everything a rule-side cell factory may use without looking up mutable run state.</summary>
public sealed record DungeonCellCreationContext(
    DungeonRunId RunId,
    DungeonLayout Layout,
    MazeCell Cell,
    int MapId,
    DungeonDifficulty Difficulty,
    DateTimeOffset StartedAt,
    IReadOnlyList<DungeonRunParticipant> Participants);

/// <summary>Builds a complete cell seed. Runtime Cell behavior never calls this service.</summary>
public interface IDungeonCellFactory
{
    ValueTask<DungeonCellSeed> CreateAsync(
        DungeonCellCreationContext context,
        CancellationToken cancellationToken);
}
