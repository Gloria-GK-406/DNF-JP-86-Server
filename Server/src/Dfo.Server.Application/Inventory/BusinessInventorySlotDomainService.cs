using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>Binds a domain slot to the existing persisted container representation.</summary>
internal sealed class BusinessInventorySlotDomainService(
    ICharacterBusinessInventoryDomainService stores) : IBusinessInventorySlotDomainService
{
    internal BusinessInventorySlotDomainService(
        ICharacterContainerDomainService containers, IInventorySlotMappingRuleService _) :
        this(new CharacterBusinessInventoryDomainService(containers)) { }

    public BoundInventorySlot? Find(CharacterId characterId, BusinessSlot slot)
    {
        _ = LegacyInventoryStorageCoordinates.ToStorage(slot);
        var container = stores.GetStore(characterId, BusinessInventoryOwnership.StoreOf(slot.Space))
            .FindContainer(slot.Space);
        return container is null ? null : new(slot, container);
    }

    public BoundInventorySlot Get(CharacterId characterId, BusinessSlot slot) =>
        this.Find(characterId, slot)
        ?? throw new InvalidOperationException(
            $"Character {characterId.Value} has no backing container for {slot.Space}/{slot.Index}.");

    public BoundInventorySlot FromStorage(InventoryStorageSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot.Container);
        var business = LegacyInventoryStorageCoordinates.ToBusiness(new(slot.Container.Type, slot.Index));
        return new(business, new BusinessItemContainer(business.Space, slot.Container));
    }
}
