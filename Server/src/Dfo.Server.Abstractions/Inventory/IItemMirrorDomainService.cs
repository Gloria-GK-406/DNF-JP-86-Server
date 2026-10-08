using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Abstractions.Inventory;

public interface IItemMirrorDomainService
{
    ValueTask<bool> RemoveAsync(IBusinessItemContainer container, ItemReference item, CancellationToken ct);
    ValueTask<bool> RemoveAsync(IItemDomainStoreSession session, ItemReference item, CancellationToken ct, bool refreshCarriedInventory = true);
}
public sealed record ItemMirrorRemoved(CharacterId CharacterId, InventoryContainerType ContainerType) : IDomainEvent
{
    public bool RefreshCarriedInventory { get; init; } = true;
}
