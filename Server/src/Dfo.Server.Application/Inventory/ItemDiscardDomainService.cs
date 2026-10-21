using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemDiscardDomainService(
    ISelectedSessionDomainService sessions,
    IBusinessInventorySlotDomainService slots,
    IItemDiscardRuleService rules, IItemLifecycleDomainService lifecycle) : IItemDiscardDomainService
{
    public async ValueTask<ItemDiscardResult> DiscardAsync(
        SelectedSessionIdentity session,
        BusinessSlot slot,
        byte entryCount,
        ushort operationType,
        int itemId,
        int count,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!sessions.IsInWorld(session))
            return new(ItemDiscardRejection.NoSession);

        var requestRejection = rules.ValidateRequest(
            slot, entryCount, operationType, itemId, count);
        if (requestRejection != ItemDiscardRejection.None)
            return new(requestRejection);

        var bound = slots.Find(session.CharacterId, slot);
        var item = bound is null
            ? null
            : await bound.Container.GetAsync(bound.Slot.Index, cancellationToken).ConfigureAwait(false);
        if (item is null)
            return new(ItemDiscardRejection.ItemUnavailable);

        var itemRejection = rules.ValidateItem(item, itemId, count);
        if (itemRejection != ItemDiscardRejection.None)
            return new(itemRejection);

        var remaining = item.Count == count ? null : (item.Snapshot with { Count = item.Count - count }).ToCore();
        var taken = await bound!.Container.TakeAsync(item, count, cancellationToken).ConfigureAwait(false);
        await lifecycle.DeleteItemAsync(taken, cancellationToken).ConfigureAwait(false);

        return new(ItemDiscardRejection.None,
            new(session, slot, item.ItemId, count, remaining, now));
    }
}
