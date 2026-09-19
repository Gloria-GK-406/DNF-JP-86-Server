using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemDiscardDomainService(
    ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers,
    IBusinessInventorySlotDomainService slots,
    IItemDiscardRuleService rules) : IItemDiscardDomainService
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
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId)
            return new(ItemDiscardRejection.NoSession);

        var requestRejection = rules.ValidateRequest(
            slot, entryCount, operationType, itemId, count);
        if (requestRejection != ItemDiscardRejection.None)
            return new(requestRejection);

        var bound = slots.Find(session.CharacterId, slot);
        var item = bound is null
            ? null
            : await bound.Container.ReadSlotAsync(bound.StorageIndex, cancellationToken).ConfigureAwait(false);
        if (item is null)
            return new(ItemDiscardRejection.ItemUnavailable);

        var itemRejection = rules.ValidateItem(item, itemId, count);
        if (itemRejection != ItemDiscardRejection.None)
            return new(itemRejection);

        InventoryItemCoreState? remaining = item.Count == count
            ? null
            : item.Core with { Value = item.Count - count };
        if (remaining is null)
            await bound!.Container.ClearAsync(bound.StorageIndex, cancellationToken).ConfigureAwait(false);
        else
            await bound!.Container.PutAsync(bound.StorageIndex, remaining, cancellationToken).ConfigureAwait(false);

        return new(ItemDiscardRejection.None,
            new(session, slot, item.ItemId, count, remaining, now));
    }
}
