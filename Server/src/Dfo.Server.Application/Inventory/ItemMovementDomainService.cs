using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>Moving a piece between the bag and what the character is wearing.</summary>
internal sealed class ItemMovementDomainService(
    IItemPlacementRuleService placementRules,
    IItemSlotRuleService slotRules, IEquipmentWearDomainService wearing) : IItemMovementDomainService
{
    public ValueTask<ItemMoveOutcome> MoveWithinBackingAsync(BoundInventorySlot source,
        BoundInventorySlot destination, int requestedCount, CancellationToken cancellationToken)
    {
        if (source.Container.StorageId != destination.Container.StorageId)
            throw new InvalidOperationException("A single-backend move requires one backing container.");
        return MoveBetweenContainersAsync(source, destination, requestedCount, cancellationToken);
    }

    public async ValueTask<ItemMoveOutcome> MoveBetweenContainersAsync(BoundInventorySlot source,
        BoundInventorySlot destination, int requestedCount, CancellationToken cancellationToken)
    {
        if (source.Container.StorageId == destination.Container.StorageId && source.Slot == destination.Slot)
            return new ItemMoveOutcome.Unchanged();

        var first = await source.Container.GetAsync(source.Slot.Index, cancellationToken).ConfigureAwait(false);
        var second = await destination.Container.GetAsync(destination.Slot.Index, cancellationToken).ConfigureAwait(false);
        var from = first is null ? destination : source;
        var into = first is null ? source : destination;
        var moving = first ?? second;
        var displaced = first is null ? null : second;
        if (moving is null) return new ItemMoveOutcome.Refused(ItemMoveRefusal.BothSlotsEmpty);

        var placement = await placementRules.PlacementForAsync(moving.ItemId, into.Slot.Space, cancellationToken)
            .ConfigureAwait(false);
        if (placement is null || !slotRules.CanOccupySlot(into.Slot, placement)
            || into.Slot.Index >= await into.Container.OpenSlotCountAsync(cancellationToken).ConfigureAwait(false))
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot);
        var moveCount = requestedCount <= 0 || requestedCount > moving.Count
            ? moving.Count : requestedCount;
        if (moving.IsPile && placement.StackLimit > 0 && moveCount > placement.StackLimit)
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.MergeExceedsStackLimit);

        // Instance metadata (notably expiry) must not turn a nonstackable same-item
        // deposit into a swap that the client's movement acknowledgement treats as a merge.
        if (moving.IsPile && displaced is { IsPile: true } && moving.ItemId == displaced.ItemId
            && placement.StackLimit == 1)
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.MergeExceedsStackLimit);

        if (displaced is null)
        {
            var record = await from.Container.TakeAsync(moving, moveCount, cancellationToken).ConfigureAwait(false);
            await into.Container.PutAsync(into.Slot.Index, record, cancellationToken).ConfigureAwait(false);
        }
        else if (moving.Snapshot.CanStackWith(displaced.Snapshot))
        {
            var limit = placement.StackLimit > 0 ? placement.StackLimit : int.MaxValue;
            if (limit <= 1 || (long)displaced.Count + moveCount > limit)
                return new ItemMoveOutcome.Refused(ItemMoveRefusal.MergeExceedsStackLimit);
            var record = await from.Container.TakeAsync(moving, moveCount, cancellationToken).ConfigureAwait(false);
            await into.Container.PutAsync(into.Slot.Index, record, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (moveCount != moving.Count)
                return new ItemMoveOutcome.Refused(ItemMoveRefusal.PartialSwapNotAllowed);
            var back = await placementRules.PlacementForAsync(displaced.ItemId, from.Slot.Space, cancellationToken)
                .ConfigureAwait(false);
            if (back is null || !slotRules.CanOccupySlot(from.Slot, back)
                || displaced.IsPile && back.StackLimit > 0 && displaced.Count > back.StackLimit
                || from.Slot.Index >= await from.Container.OpenSlotCountAsync(cancellationToken).ConfigureAwait(false))
                return new ItemMoveOutcome.Refused(ItemMoveRefusal.DisplacedCannotOccupySlot);
            var movingRecord = await from.Container.TakeAsync(moving, null, cancellationToken).ConfigureAwait(false);
            var displacedRecord = await into.Container.TakeAsync(displaced, null, cancellationToken).ConfigureAwait(false);
            await into.Container.PutAsync(into.Slot.Index, movingRecord, cancellationToken).ConfigureAwait(false);
            await from.Container.PutAsync(from.Slot.Index, displacedRecord, cancellationToken).ConfigureAwait(false);
        }

        await from.Container.FlushAsync(cancellationToken).ConfigureAwait(false);
        return new ItemMoveOutcome.Moved(moveCount, moving.ItemId);
    }

    public ValueTask<bool> CanWearAsync(Dfo.Server.Data.Characters.CharacterId characterId, int itemId, CancellationToken cancellationToken) =>
        wearing.CanWearAsync(characterId, itemId, cancellationToken);

    public ValueTask<BusinessSlot?> WornSlotForAsync(
        int itemId, CancellationToken cancellationToken) =>
        placementRules.WornSlotForAsync(itemId, cancellationToken);

    public async ValueTask<SlotAdmission> MayOccupySlotAsync(
        BusinessSlot slot,
        int itemId,
        CancellationToken cancellationToken)
    {
        var placement = await placementRules.PlacementForAsync(itemId, slot.Space, cancellationToken)
            .ConfigureAwait(false);
        if (placement is null)
            return SlotAdmission.ItemUnknown;

        return slotRules.CanOccupySlot(slot, placement)
            ? SlotAdmission.Allowed
            : SlotAdmission.SlotNotAllowed;
    }
}
