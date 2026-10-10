using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemSaleDomainService(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ITownAreaDomainService townAreas, ICharacterDomainService characters,
    IBusinessInventorySlotDomainService slots, IItemSaleRuleService rules,
    IDungeonRunDomainService runs, IItemLifecycleDomainService lifecycle,
    ILogger<ItemSaleDomainService> logger) : IItemSaleDomainService
{
    public async ValueTask<ItemSaleResult> SellAsync(SelectedSessionIdentity session, BusinessSlot slot,
        int count, int npcId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId)
            return new(ItemSaleRejection.NoSession);
        // Worn pieces, avatars and creatures require their own appearance/lifecycle updates.
        if (!slot.Space.IsCarriedItemSpace() || count <= 0)
            return new(ItemSaleRejection.InvalidRequest);
        var run = runs.Find(session.CharacterId);
        var inTown = run is null && townAreas.Find(session.CharacterId) is { } member && session.Owns(member);
        var dungeonCleared = run is { IsBossRoom: true, IsCleared: true, ClearOpen: true }
            && run.Participants.Any(p => p.AccountId == session.AccountId && p.CharacterId == session.CharacterId);
        if (!await rules.CanAccessShopAsync(npcId, inTown, dungeonCleared, cancellationToken).ConfigureAwait(false))
            return new(ItemSaleRejection.ShopUnavailable);
        var bound = slots.Find(session.CharacterId, slot);
        var item = bound is null
            ? null
            : await bound.Container.GetAsync(bound.Slot.Index, cancellationToken).ConfigureAwait(false);
        if (item is null)
            return new(ItemSaleRejection.ItemUnavailable);
        var quote = await rules.QuoteAsync(item, count, cancellationToken).ConfigureAwait(false);
        if (quote.Rejection != ItemSaleRejection.None)
            return new(quote.Rejection);
        var character = characters.Get(session.CharacterId);
        if (await character.IdentityAsync(cancellationToken).ConfigureAwait(false) is null)
            return new(ItemSaleRejection.NoSession);
        if (quote.PricingFailure is { } failure)
            logger.LogError("NPC sale price calculation failed; using zero gold. Character={CharacterId} "
                + "ItemId={ItemId} ItemUid={ItemUid} Slot={Slot} Count={Count} Reason={Reason} ItemState={ItemState}",
                session.CharacterId.Value, item.ItemId, item.Snapshot.ItemUid, slot, quote.Count, failure, item.Snapshot);
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
        var remaining = item.Count == quote.Count ? null : (item.Snapshot with { Count = item.Count - quote.Count }).ToCore();
        var taken = await bound!.Container.TakeAsync(item, quote.Count, cancellationToken).ConfigureAwait(false);
        await lifecycle.DeleteItemAsync(taken, cancellationToken).ConfigureAwait(false);

        return new(ItemSaleRejection.None,
            new(session, slot, item.ItemId, quote.Count, gold, remaining, now));
    }
}
