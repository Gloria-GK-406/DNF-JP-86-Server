using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
namespace Dfo.Server.Application.Inventory;

internal sealed class ItemMirrorDomainService(IItemLifecycleDomainService lifecycle, IEventService events) : IItemMirrorDomainService
{
    public async ValueTask<bool> RemoveAsync(IBusinessItemContainer container, ItemReference item, CancellationToken ct)
    {
        if (!item.Snapshot.IsMirror) return false;
        await RemoveAsync(await container.TakeAsync(item, null, ct).ConfigureAwait(false), item, true, ct).ConfigureAwait(false);
        await container.FlushAsync(ct).ConfigureAwait(false);
        return true;
    }
    public async ValueTask<bool> RemoveAsync(IItemDomainStoreSession session, ItemReference item, CancellationToken ct, bool refreshCarriedInventory = true)
    {
        if (!item.Snapshot.IsMirror) return false;
        await RemoveAsync(await session.TakeAsync(item, null, ct).ConfigureAwait(false), item, refreshCarriedInventory, ct).ConfigureAwait(false);
        return true;
    }
    private async ValueTask RemoveAsync(ItemRecord record, ItemReference item, bool refreshCarriedInventory, CancellationToken ct)
    {
        await lifecycle.DeleteItemAsync(record, ct).ConfigureAwait(false);
        if (item.ContainerId.TryGetCharacterOwner(out var character, out var type))
            await events.EnqueueAsync(new ItemMirrorRemoved(character, type) { RefreshCarriedInventory = refreshCarriedInventory }, ct).ConfigureAwait(false);
    }
}
