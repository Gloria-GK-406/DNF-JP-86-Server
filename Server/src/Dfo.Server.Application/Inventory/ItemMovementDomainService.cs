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

        var first = await source.Container.ReadSlotAsync(source.Slot.Index, cancellationToken).ConfigureAwait(false);
        var second = await destination.Container.ReadSlotAsync(destination.Slot.Index, cancellationToken).ConfigureAwait(false);
        var from = first is null ? destination : source;
        var into = first is null ? source : destination;
        var moving = first ?? second;
        var displaced = first is null ? null : second;
        if (moving is null) return new ItemMoveOutcome.Refused(ItemMoveRefusal.BothSlotsEmpty);

        var placement = await placementRules.PlacementForAsync(moving.ItemId, cancellationToken)
            .ConfigureAwait(false);
        if (placement is null || !slotRules.CanOccupySlot(into.Slot, placement)
            || into.Slot.Index >= await into.Container.OpenSlotCountAsync(cancellationToken).ConfigureAwait(false))
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot);
        var moveCount = requestedCount <= 0 || requestedCount > moving.Count
            ? moving.Count : requestedCount;

        if (displaced is null)
        {
            if (moveCount == moving.Count)
            {
                await PutWholeAsync(into, from, moving, cancellationToken).ConfigureAwait(false);
                await from.Container.ClearAsync(from.Slot.Index, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await from.Container.PutAsync(from.Slot.Index,
                    moving.Core with { Value = moving.Count - moveCount }, cancellationToken).ConfigureAwait(false);
                await into.Container.PutAsync(into.Slot.Index,
                    moving.Core with { Value = moveCount }, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else if (moving.IsPile && displaced.IsPile && displaced.ItemId == moving.ItemId
            && moving.Core with { Value = 0 } == displaced.Core with { Value = 0 })
        {
            var limit = placement.StackLimit > 0 ? placement.StackLimit : int.MaxValue;
            if (limit <= 1 || (long)displaced.Count + moveCount > limit)
                return new ItemMoveOutcome.Refused(ItemMoveRefusal.MergeExceedsStackLimit);
            await into.Container.PutAsync(into.Slot.Index,
                displaced.Core with { Value = checked(displaced.Count + moveCount) }, cancellationToken)
                .ConfigureAwait(false);
            if (moveCount == moving.Count)
                await from.Container.ClearAsync(from.Slot.Index, cancellationToken).ConfigureAwait(false);
            else
                await from.Container.PutAsync(from.Slot.Index,
                    moving.Core with { Value = moving.Count - moveCount }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (moveCount != moving.Count)
                return new ItemMoveOutcome.Refused(ItemMoveRefusal.PartialSwapNotAllowed);
            var back = await placementRules.PlacementForAsync(displaced.ItemId, cancellationToken)
                .ConfigureAwait(false);
            if (back is null || !slotRules.CanOccupySlot(from.Slot, back)
                || from.Slot.Index >= await from.Container.OpenSlotCountAsync(cancellationToken).ConfigureAwait(false))
                return new ItemMoveOutcome.Refused(ItemMoveRefusal.DisplacedCannotOccupySlot);
            await PutWholeAsync(into, from, moving, cancellationToken).ConfigureAwait(false);
            await PutWholeAsync(from, into, displaced, cancellationToken).ConfigureAwait(false);
        }

        await from.Container.FlushAsync(cancellationToken).ConfigureAwait(false);
        return new ItemMoveOutcome.Moved(moveCount, moving.ItemId);
    }

    private static ValueTask PutWholeAsync(BoundInventorySlot destination,
        BoundInventorySlot source, ContainerSlotItem item, CancellationToken cancellationToken) =>
        item.IsPile
            ? destination.Container.PutAsync(destination.Slot.Index, item.Core, cancellationToken)
            : destination.Container.PutTransferredAsync(destination.Slot.Index, item.Core,
                source.Container, source.Slot.Index, cancellationToken);

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
        var placement = await placementRules.PlacementForAsync(itemId, cancellationToken)
            .ConfigureAwait(false);
        if (placement is null)
            return SlotAdmission.ItemUnknown;

        return slotRules.CanOccupySlot(slot, placement)
            ? SlotAdmission.Allowed
            : SlotAdmission.SlotNotAllowed;
    }
}
