using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>Moving a piece between the bag and what the character is wearing.</summary>
internal sealed class ItemMovementDomainService(
    IItemPlacementRuleService placementRules,
    IItemSlotRuleService slotRules) : IItemMovementDomainService
{
    public ValueTask<byte?> WornSlotForAsync(int itemId, CancellationToken cancellationToken) =>
        placementRules.WornSlotForAsync(itemId, cancellationToken);

    public async ValueTask<SlotAdmission> MayOccupySlotAsync(
        InventoryContainerType containerType,
        int itemId,
        int slotIndex,
        CancellationToken cancellationToken)
    {
        var placement = await placementRules.PlacementForAsync(itemId, cancellationToken)
            .ConfigureAwait(false);
        if (placement is null)
            return SlotAdmission.ItemUnknown;

        return slotRules.CanOccupySlot(containerType, placement, slotIndex)
            ? SlotAdmission.Allowed
            : SlotAdmission.SlotNotAllowed;
    }
}
