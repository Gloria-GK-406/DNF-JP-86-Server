using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Identifies one container among the several a character may hold of the same type.</summary>
/// <remarks>
/// A string rather than a number because the identifiers come from unrelated places -- the
/// character's own storage, a dungeon run, a trade -- and a shared numeric space would make two of
/// them collide the moment a second producer appears.
/// </remarks>
public readonly record struct ContainerId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// What the client needs in order to draw a container: which list it is, and its capacity
/// parameter.
/// </summary>
/// <remarks>
/// Capacity is per character rather than per type -- a character who expanded their cargo has a
/// larger one -- so the shape travels with the container instead of being looked up from the type.
/// This is also what a transient container is built from: an empty container of the same shape is
/// one the client will draw exactly as it drew the original.
/// </remarks>
public sealed record ContainerShape(InventoryContainerType ContainerType, ushort Parameter);

/// <summary>
/// A set of items addressed by slot, which can be replaced by another implementation.
/// </summary>
/// <remarks>
/// The container is bound to one character. Its methods therefore do not take a character: an
/// instance that served several characters would have to be trusted to keep them apart, and every
/// caller would have to keep passing the one fact the container already knows.
///
/// Compound operations live here rather than above, because the container owns its own storage
/// semantics. A move that displaces another item is one change to the world, and only the
/// container knows how to make it atomic -- a caller composing reads and writes from outside could
/// not.
/// </remarks>
public interface IItemContainer
{
    ContainerId Id { get; }

    InventoryContainerType Type { get; }

    ContainerShape Shape { get; }

    /// <summary>
    /// Whether this container may be removed from the character.
    /// </summary>
    /// <remarks>
    /// False for the container a character falls back to. Removing that one would leave the
    /// character with nothing bound -- a state with no recovery, in a game with no reconnect --
    /// so it is refused rather than left to discipline.
    /// </remarks>
    bool IsRemovable { get; }

    /// <summary>What one slot holds, or null when it is empty.</summary>
    ValueTask<ContainerSlotItem?> ReadSlotAsync(int slotIndex, CancellationToken cancellationToken);

    /// <summary>What two slots hold, read together.</summary>
    ValueTask<ContainerSlotPair> ReadSlotsAsync(
        int firstSlotIndex,
        int secondSlotIndex,
        CancellationToken cancellationToken);

    /// <summary>Every occupied slot, for projection to the client.</summary>
    ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Applies a decided move, or answers false when the slots no longer hold what the caller read.
    /// </summary>
    /// <remarks>
    /// False, never an exception, for a plan this container will not carry out -- whether because
    /// the world moved on or because the plan asks for something it does not do. Every container
    /// refuses the same plans, which is what lets one be exchanged for another.
    /// </remarks>
    ValueTask<bool> ApplyMoveAsync(ContainerMovePlan plan, CancellationToken cancellationToken);

    /// <summary>
    /// Takes items out of one pile, or answers null when that pile is no longer the one the caller
    /// read.
    /// </summary>
    ValueTask<ContainerConsumption?> ConsumeAsync(
        long storageEntryId,
        int count,
        CancellationToken cancellationToken);
}

/// <summary>What one slot of a container holds.</summary>
public sealed record ContainerSlotItem(
    long StorageEntryId, int SlotIndex, byte ItemKind, int ItemId, int Count);

/// <summary>What the two slots named by a move request hold right now.</summary>
public sealed record ContainerSlotPair(ContainerSlotItem? First, ContainerSlotItem? Second);

/// <summary>How a decided move rearranges the two slots.</summary>
public enum ContainerMoveKind
{
    /// <summary>The whole stack goes to an empty slot.</summary>
    Relocate,

    /// <summary>Part of the stack goes to an empty slot; the rest stays behind.</summary>
    Split,

    /// <summary>The two slots trade places.</summary>
    Swap,

    /// <summary>The stack is poured into a pile of the same item.</summary>
    Merge,
}

/// <summary>
/// A move the caller has already decided on, for the container to apply as one change.
/// </summary>
/// <param name="DisplacedEntryId">
/// What the caller saw in the target slot, or null when it saw the slot empty. The container
/// rechecks it and refuses the move when the slot has since changed, so a decision taken from an
/// earlier read cannot be applied against a world that has moved on.
/// </param>
public sealed record ContainerMovePlan(
    long MovingEntryId,
    int TargetSlotIndex,
    int MoveCount,
    long? DisplacedEntryId,
    ContainerMoveKind Kind);

/// <summary>What one slot held after something was taken out of it.</summary>
/// <param name="RemainingCount">Zero when the last of the pile went and the slot is now empty.</param>
public sealed record ContainerConsumption(int SlotIndex, int ItemId, int RemainingCount);

/// <summary>Builds a container that holds nothing and outlives nothing.</summary>
/// <remarks>
/// Shape comes from an existing container rather than from the type, because capacity is a fact
/// about the character. Handing it in also keeps the factory from having to know how any
/// particular container type is sized.
/// </remarks>
public interface IItemContainerFactory
{
    IItemContainer CreateTransient(ContainerId id, ContainerShape shape);
}
