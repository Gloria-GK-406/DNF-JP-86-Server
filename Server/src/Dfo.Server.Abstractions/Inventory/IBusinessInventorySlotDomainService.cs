using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>A business slot bound to the current backing container used by one character.</summary>
public sealed record BoundInventorySlot(BusinessSlot Slot, IBusinessItemContainer Container);

/// <summary>An indivisible address inside one concrete persistence container.</summary>
public readonly record struct InventoryStorageSlot(IItemContainer Container, int Index);

/// <summary>Resolves logical inventory addresses without exposing game coordinates to callers.</summary>
public interface IBusinessInventorySlotDomainService
{
    BoundInventorySlot? Find(CharacterId characterId, BusinessSlot slot);

    BoundInventorySlot Get(CharacterId characterId, BusinessSlot slot);

    /// <summary>Projects an existing backing address into the business address vocabulary.</summary>
    BoundInventorySlot FromStorage(InventoryStorageSlot slot);

}
