using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>Binds a domain slot to the existing persisted container representation.</summary>
internal sealed class BusinessInventorySlotDomainService(
    ICharacterContainerDomainService containers,
    IInventorySlotMappingRuleService mappings) : IBusinessInventorySlotDomainService
{
    public BoundInventorySlot? Find(CharacterId characterId, BusinessSlot slot)
    {
        var stored = mappings.ToGameSlot(slot);
        var container = containers.FindContainer(characterId, stored.Space);
        return container is null ? null : new(slot, container, stored.Index);
    }

    public BoundInventorySlot Get(CharacterId characterId, BusinessSlot slot) =>
        this.Find(characterId, slot)
        ?? throw new InvalidOperationException(
            $"Character {characterId.Value} has no backing container for {slot.Space}/{slot.Index}.");

    public BoundInventorySlot FromStorage(InventoryStorageSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot.Container);
        var business = mappings.ToBusinessSlot(slot);
        return new(business, slot.Container, slot.Index);
    }
}
