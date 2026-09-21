using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>Moving a piece between the bag and what the character is wearing.</summary>
internal sealed class ItemMovementDomainService(
    IItemPlacementRuleService placementRules,
    IItemSlotRuleService slotRules, IEquipmentWearDomainService wearing) : IItemMovementDomainService
{
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
