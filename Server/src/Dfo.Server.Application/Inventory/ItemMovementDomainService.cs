using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>Moving a piece between the bag and what the character is wearing.</summary>
internal sealed class ItemMovementDomainService(
    IItemPlacementRuleService placementRules,
    IItemSlotRuleService slotRules) : IItemMovementDomainService
{
    public ValueTask<byte?> WornSlotForAsync(int itemId, CancellationToken cancellationToken) =>
        placementRules.WornSlotForAsync(itemId, cancellationToken);

    public async ValueTask<MainSlotAdmission> MayOccupyMainSlotAsync(
        int itemId, int slotIndex, CancellationToken cancellationToken)
    {
        var placement = await placementRules.PlacementForAsync(itemId, cancellationToken)
            .ConfigureAwait(false);
        if (placement is null)
            return MainSlotAdmission.ItemUnknown;

        return slotRules.CanOccupyMainSlot(placement, slotIndex)
            ? MainSlotAdmission.Allowed
            : MainSlotAdmission.SlotNotAllowed;
    }
}
