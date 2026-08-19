using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// The stack shape a container writes when it has to produce an item row of its own.
/// </summary>
/// <remarks>
/// A split makes a second pile out of one. The pile it makes carries only what a pile is -- kind,
/// identifier, count -- because everything else a core can hold describes an individual piece of
/// gear, and gear does not split. Persistent storage writes exactly this shape, so a transient
/// container that wrote a copy of the original instead would answer differently for the same move.
/// </remarks>
internal static class StackableItemCore
{
    private static readonly ChronicleOptionState NoChronicle = new(0, 0, 0, 0, 0);

    private static readonly RandomOptionState NoRandom = new(0, 0, 0);

    private static readonly InventoryItemCoreState Empty = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        NoChronicle, NoChronicle, 0, 0, 0, 0, 0,
        NoRandom, NoRandom, NoRandom, 0, 0, 0, NoRandom,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static InventoryItemCoreState Create(byte itemKind, int itemId, int count) =>
        Empty with
        {
            ItemKind = itemKind,
            ItemId = itemId,
            Value = count,
            Marker16 = -1,
            RandomOptionChangedIndex = byte.MaxValue,
        };
}

/// <summary>
/// A container whose items live in the character's persistent storage.
/// </summary>
/// <remarks>
/// The character's own container, and therefore the one everything falls back to: it is the only
/// one whose contents outlive the session, so a character left bound to anything else would come
/// back to an inventory that no longer exists.
/// </remarks>
internal sealed class PersistentItemContainer(
    ContainerId id,
    CharacterId characterId,
    ContainerShape shape,
    IEntryInventoryMutationRepository storage) : IItemContainer
{
    public ContainerId Id => id;

    public InventoryContainerType Type => shape.ContainerType;

    public ContainerShape Shape => shape;

    public bool IsRemovable => false;

    public async ValueTask<ContainerSlotItem?> ReadSlotAsync(
        int slotIndex, CancellationToken cancellationToken) =>
        ToSlotItem(await storage.ReadMainSlotAsync(characterId, slotIndex, cancellationToken)
            .ConfigureAwait(false));

    public async ValueTask<ContainerSlotPair> ReadSlotsAsync(
        int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken)
    {
        var pair = await storage.ReadMainSlotsAsync(
            characterId, firstSlotIndex, secondSlotIndex, cancellationToken).ConfigureAwait(false);
        return new ContainerSlotPair(ToSlotItem(pair.Source), ToSlotItem(pair.Destination));
    }

    public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
        CancellationToken cancellationToken) =>
        storage.ReadMainItemsAsync(characterId, cancellationToken);

    public ValueTask<bool> ApplyMoveAsync(
        ContainerMovePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.DisplacedEntryId == plan.MovingEntryId || ToStorageKind(plan.Kind) is not { } kind)
            return ValueTask.FromResult(false);

        return storage.ApplyMainMoveAsync(
            characterId,
            new MainInventoryMovePlan(
                plan.MovingEntryId,
                plan.TargetSlotIndex,
                plan.MoveCount,
                plan.DisplacedEntryId,
                kind),
            cancellationToken);
    }

    /// <summary>Names each kind across to storage's own enumeration, or null for one it has not.</summary>
    /// <remarks>
    /// Written out rather than cast. The two enumerations are declared apart and a cast would agree
    /// with them only by position, so a member added to either would quietly turn every swap into a
    /// merge -- which deletes one of the two items, with nothing to fail on.
    ///
    /// A kind storage does not know is refused rather than raised, so that a kind added on one side
    /// only fails a move rather than killing whichever frame happens to carry it.
    /// </remarks>
    private static MainInventoryMoveKind? ToStorageKind(ContainerMoveKind kind) => kind switch
    {
        ContainerMoveKind.Relocate => MainInventoryMoveKind.Relocate,
        ContainerMoveKind.Split => MainInventoryMoveKind.Split,
        ContainerMoveKind.Swap => MainInventoryMoveKind.Swap,
        ContainerMoveKind.Merge => MainInventoryMoveKind.Merge,
        _ => null,
    };

    public async ValueTask<ContainerConsumption?> ConsumeAsync(
        long storageEntryId, int count, CancellationToken cancellationToken)
    {
        var consumed = await storage.ConsumeMainItemAsync(
            characterId, storageEntryId, count, cancellationToken).ConfigureAwait(false);
        return consumed is null
            ? null
            : new ContainerConsumption(consumed.SlotIndex, consumed.ItemId, consumed.RemainingCount);
    }

    private static ContainerSlotItem? ToSlotItem(MainInventoryOccupant? occupant) =>
        occupant is null
            ? null
            : new ContainerSlotItem(
                occupant.StorageEntryId, occupant.SlotIndex, occupant.ItemKind,
                occupant.ItemId, occupant.Count);
}

/// <summary>
/// A container that holds nothing beyond this run.
/// </summary>
/// <remarks>
/// What a dungeon hands the player is this: an inventory the client draws exactly like the real
/// one, filled from what the run produced, and gone when the run is. Nothing about it reaches
/// storage, so leaving one behind loses only what was never meant to be kept.
///
/// It is built from a shape rather than from a character, so nothing in its own signature holds it
/// to one of them; the item list is guarded rather than left to an assumption the constructor is in
/// no position to make.
/// </remarks>
internal sealed class TransientItemContainer(ContainerId id, ContainerShape shape) : IItemContainer
{
    private readonly List<InventoryItemState> items = [];

    private readonly object sync = new();

    private long nextEntryId = 1;

    public ContainerId Id => id;

    public InventoryContainerType Type => shape.ContainerType;

    public ContainerShape Shape => shape;

    public bool IsRemovable => true;

    public ValueTask<ContainerSlotItem?> ReadSlotAsync(
        int slotIndex, CancellationToken cancellationToken)
    {
        lock (sync)
            return ValueTask.FromResult(ToSlotItem(FindBySlot(slotIndex)));
    }

    public ValueTask<ContainerSlotPair> ReadSlotsAsync(
        int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            return ValueTask.FromResult(new ContainerSlotPair(
                ToSlotItem(FindBySlot(firstSlotIndex)), ToSlotItem(FindBySlot(secondSlotIndex))));
        }
    }

    public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
        CancellationToken cancellationToken)
    {
        lock (sync)
        {
            return ValueTask.FromResult<IReadOnlyList<InventoryItemState>>(
                items.OrderBy(item => item.SlotIndex).ThenBy(item => item.StorageEntryId).ToArray());
        }
    }

    /// <summary>Applies a decided move under the same rules persistent storage applies.</summary>
    public ValueTask<bool> ApplyMoveAsync(ContainerMovePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        lock (sync)
            return ValueTask.FromResult(Apply(plan));
    }

    private bool Apply(ContainerMovePlan plan)
    {
        // A plan naming one item as both ends is not a move. Storage and memory would each do
        // something defensible with it and they would not be the same something.
        if (plan.DisplacedEntryId == plan.MovingEntryId)
            return false;

        var moving = FindByEntry(plan.MovingEntryId);
        if (moving is null || plan.MoveCount <= 0 || plan.MoveCount > moving.Core.Value)
            return false;

        var displaced = FindBySlot(plan.TargetSlotIndex);
        if (displaced?.StorageEntryId != plan.DisplacedEntryId)
            return false;

        switch (plan.Kind)
        {
            case ContainerMoveKind.Relocate
                when displaced is null && plan.MoveCount == moving.Core.Value:
                Replace(moving, moving with { SlotIndex = plan.TargetSlotIndex });
                break;

            case ContainerMoveKind.Split when displaced is null && plan.MoveCount < moving.Core.Value:
                Replace(moving, WithCount(moving, moving.Core.Value - plan.MoveCount));
                items.Add(new InventoryItemState(
                    nextEntryId++,
                    plan.TargetSlotIndex,
                    StackableItemCore.Create(
                        moving.Core.ItemKind, moving.Core.ItemId, plan.MoveCount)));
                break;

            case ContainerMoveKind.Swap when displaced is not null && plan.MoveCount == moving.Core.Value:
                var vacated = moving.SlotIndex;
                Replace(moving, moving with { SlotIndex = plan.TargetSlotIndex });
                Replace(displaced, displaced with { SlotIndex = vacated });
                break;

            case ContainerMoveKind.Merge
                when displaced is not null && displaced.Core.ItemId == moving.Core.ItemId:
                Replace(displaced, WithCount(displaced, checked(displaced.Core.Value + plan.MoveCount)));
                if (plan.MoveCount == moving.Core.Value)
                    items.Remove(moving);
                else
                    Replace(moving, WithCount(moving, moving.Core.Value - plan.MoveCount));
                break;

            default:
                return false;
        }

        return true;
    }

    public ValueTask<ContainerConsumption?> ConsumeAsync(
        long storageEntryId, int count, CancellationToken cancellationToken)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "A consumed count must be positive.");

        lock (sync)
        {
            var item = FindByEntry(storageEntryId);
            if (item is null || count > item.Core.Value)
                return ValueTask.FromResult<ContainerConsumption?>(null);

            var remaining = item.Core.Value - count;
            if (remaining == 0)
                items.Remove(item);
            else
                Replace(item, WithCount(item, remaining));

            return ValueTask.FromResult<ContainerConsumption?>(
                new ContainerConsumption(item.SlotIndex, item.Core.ItemId, remaining));
        }
    }

    /// <summary>Puts an item into a named slot, for filling the container before it is used.</summary>
    public ContainerSlotItem Place(int slotIndex, byte itemKind, int itemId, int count)
    {
        lock (sync)
        {
            if (FindBySlot(slotIndex) is not null)
            {
                throw new InvalidOperationException(
                    $"Transient container slot {slotIndex} is occupied.");
            }

            var placed = new InventoryItemState(
                nextEntryId++, slotIndex, StackableItemCore.Create(itemKind, itemId, count));
            items.Add(placed);
            return ToSlotItem(placed)!;
        }
    }

    private InventoryItemState? FindBySlot(int slotIndex) =>
        items.SingleOrDefault(item => item.SlotIndex == slotIndex);

    private InventoryItemState? FindByEntry(long storageEntryId) =>
        items.SingleOrDefault(item => item.StorageEntryId == storageEntryId);

    private void Replace(InventoryItemState current, InventoryItemState replacement) =>
        items[items.IndexOf(current)] = replacement;

    private static InventoryItemState WithCount(InventoryItemState item, int count) =>
        item with { Core = item.Core with { Value = count } };

    private static ContainerSlotItem? ToSlotItem(InventoryItemState? item) =>
        item is null
            ? null
            : new ContainerSlotItem(
                item.StorageEntryId, item.SlotIndex, item.Core.ItemKind, item.Core.ItemId,
                item.Core.Value);
}

internal sealed class ItemContainerFactory : IItemContainerFactory
{
    public IItemContainer CreateTransient(ContainerId id, ContainerShape shape) =>
        new TransientItemContainer(id, shape);
}
