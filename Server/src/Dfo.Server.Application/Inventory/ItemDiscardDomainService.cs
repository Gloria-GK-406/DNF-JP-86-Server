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
    ICharacterContainerDomainService containers,
    IItemDiscardRuleService rules) : IItemDiscardDomainService
{
    public async ValueTask<ItemDiscardResult> DiscardAsync(
        SelectedSessionIdentity session,
        InventoryContainerType container,
        byte entryCount,
        ushort operationType,
        int slotIndex,
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
            container, entryCount, operationType, slotIndex, itemId, count);
        if (requestRejection != ItemDiscardRejection.None)
            return new(requestRejection);

        var bag = containers.FindContainer(session.CharacterId, container);
        var item = bag is null
            ? null
            : await bag.ReadSlotAsync(slotIndex, cancellationToken).ConfigureAwait(false);
        if (item is null)
            return new(ItemDiscardRejection.ItemUnavailable);

        var itemRejection = rules.ValidateItem(item, itemId, count);
        if (itemRejection != ItemDiscardRejection.None)
            return new(itemRejection);

        InventoryItemCoreState? remaining = item.Count == count
            ? null
            : item.Core with { Value = item.Count - count };
        if (remaining is null)
            await bag!.ClearAsync(slotIndex, cancellationToken).ConfigureAwait(false);
        else
            await bag!.PutAsync(slotIndex, remaining, cancellationToken).ConfigureAwait(false);

        return new(ItemDiscardRejection.None,
            new(session, container, slotIndex, item.ItemId, count, remaining, now));
    }
}
