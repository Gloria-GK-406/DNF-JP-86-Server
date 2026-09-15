using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemSaleDomainService(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ITownAreaDomainService townAreas, ICharacterDomainService characters,
    ICharacterContainerDomainService containers, IItemSaleRuleService rules) : IItemSaleDomainService
{
    public async ValueTask<ItemSaleResult> SellAsync(SelectedSessionIdentity session, InventoryContainerType container,
        int slotIndex, int count, int npcId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId)
            return new(ItemSaleRejection.NoSession);
        // Worn pieces, avatars and creatures require their own appearance/lifecycle updates.
        if (container != InventoryContainerType.Main || slotIndex is < 3 or >= 354 || count <= 0)
            return new(ItemSaleRejection.InvalidRequest);
        if (townAreas.Find(session.CharacterId) is not { } member
            || !session.Owns(member)
            || !await rules.HasShopAsync(npcId, cancellationToken).ConfigureAwait(false))
            return new(ItemSaleRejection.ShopUnavailable);
        var bag = containers.FindContainer(session.CharacterId, container);
        var item = bag is null ? null : await bag.ReadSlotAsync(slotIndex, cancellationToken).ConfigureAwait(false);
        if (item is null)
            return new(ItemSaleRejection.ItemUnavailable);
        var quote = await rules.QuoteAsync(item, count, cancellationToken).ConfigureAwait(false);
        if (quote.Rejection != ItemSaleRejection.None)
            return new(quote.Rejection);
        var character = characters.Get(session.CharacterId);
        if (await character.IdentityAsync(cancellationToken).ConfigureAwait(false) is null)
            return new(ItemSaleRejection.NoSession);
        var gold = await character.GoldAsync(cancellationToken).ConfigureAwait(false);
        var targetGold = (long)gold + quote.Gold;
        if (targetGold > int.MaxValue)
            return new(ItemSaleRejection.GoldLimit);
        if (quote.Gold > 0)
        {
            gold = await character.AddGoldAsync(quote.Gold, cancellationToken).ConfigureAwait(false);
            // AddGold caps ordinary rewards. A sale must receive the whole price or roll back.
            if (gold != targetGold)
                throw new ItemSaleRejectedException(ItemSaleRejection.GoldLimit);
        }
        var remaining = item.Count == quote.Count ? null : item.Core with { Value = item.Count - quote.Count };
        if (remaining is null)
            await bag!.ClearAsync(slotIndex, cancellationToken).ConfigureAwait(false);
        else
            await bag!.PutAsync(slotIndex, remaining, cancellationToken).ConfigureAwait(false);
        return new(ItemSaleRejection.None,
            new(session, container, slotIndex, item.ItemId, quote.Count, gold, remaining, now));
    }
}
