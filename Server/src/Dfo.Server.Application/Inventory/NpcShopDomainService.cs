using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class NpcShopDomainService(
    ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers,
    ITownAreaDomainService townAreas,
    ICharacterDomainService characters,
    IAccountTable accounts,
    ICharacterContainerDomainService containers,
    INpcShopRuleService rules,
    INpcShopPurchaseTable purchases) : INpcShopDomainService
{
    public async ValueTask<NpcShopPurchaseResult> PurchaseAsync(
        SelectedSessionIdentity session, int itemId, int rawCount, int shopId, int npcId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!IsCurrent(session))
            return NpcShopPurchaseResult.Refused(NpcShopRejection.NoSession);
        if (townAreas.Find(session.CharacterId) is not { } member || !session.Owns(member))
            return NpcShopPurchaseResult.Refused(NpcShopRejection.ShopUnavailable);
        var character = characters.Get(session.CharacterId);
        var identity = await character.IdentityAsync(cancellationToken).ConfigureAwait(false);
        var bag = containers.FindContainer(session.CharacterId, InventoryContainerType.Main);
        if (identity is null || bag is null)
            return NpcShopPurchaseResult.Refused(NpcShopRejection.NoSession);

        var priced = await rules.QuoteAsync(npcId, shopId, identity.Job, itemId, rawCount, cancellationToken)
            .ConfigureAwait(false);
        if (priced.Quote is not { } quote)
            return NpcShopPurchaseResult.Refused(priced.Rejection);

        NpcShopPurchaseKey? purchaseKey = null;
        if (quote.Definition.Limit is { } limit)
        {
            purchaseKey = Key(session, npcId, itemId, limit, now);
            var bought = await purchases.ReadAsync(purchaseKey, cancellationToken).ConfigureAwait(false);
            if (quote.Count > limit.Count - bought)
                return NpcShopPurchaseResult.Refused(NpcShopRejection.PurchaseLimitReached);
        }

        var gold = await character.GoldAsync(cancellationToken).ConfigureAwait(false);
        var held = await bag.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var planned = rules.Plan(quote, held, gold, Random.Shared.Next(1, int.MaxValue));
        if (planned.Plan is not { } plan)
            return NpcShopPurchaseResult.Refused(planned.Rejection);

        var skillPoints = await character.RemainingSkillPointsAsync(cancellationToken).ConfigureAwait(false);
        if (skillPoints is null)
            return NpcShopPurchaseResult.Refused(NpcShopRejection.NoSession);
        var wallet = await accounts.FindWalletAsync(session.AccountId, cancellationToken).ConfigureAwait(false);
        if (wallet is null)
            return NpcShopPurchaseResult.Refused(NpcShopRejection.NoSession);
        var cera = checked((int)wallet.Cera);

        if (quote.GoldCost > 0)
            gold = await character.TrySpendGoldAsync(quote.GoldCost, cancellationToken).ConfigureAwait(false)
                ?? throw new NpcShopPurchaseRejectedException(NpcShopRejection.InsufficientGold);
        foreach (var change in plan.MaterialChanges)
        {
            if (change.Core is null)
                await bag.ClearAsync(change.SlotIndex, cancellationToken).ConfigureAwait(false);
            else
                await bag.PutAsync(change.SlotIndex, change.Core, cancellationToken).ConfigureAwait(false);
        }
        foreach (var grant in plan.GrantedSlots)
            await bag.PutAsync(grant.Item.SlotIndex, grant.Item.Core, cancellationToken).ConfigureAwait(false);
        if (purchaseKey is not null
            && !await purchases.TryAddAsync(purchaseKey, quote.Count, quote.Definition.Limit!.Count, cancellationToken)
                .ConfigureAwait(false))
            throw new NpcShopPurchaseRejectedException(NpcShopRejection.PurchaseLimitReached);

        return new(NpcShopRejection.None, new(session, itemId, quote.Count, gold, skillPoints.Page0Sp,
            cera, quote.GoldCost > 0, plan.MaterialChanges, plan.GrantedSlots, now));
    }

    public async ValueTask<NpcShopPurchaseCountsResult> PurchaseCountsAsync(
        SelectedSessionIdentity session, int npcId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!IsCurrent(session))
            return new(NpcShopRejection.NoSession, []);
        var identity = await characters.Get(session.CharacterId).IdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity is null)
            return new(NpcShopRejection.NoSession, []);
        var limited = await rules.LimitedItemsAsync(npcId, identity.Job, cancellationToken).ConfigureAwait(false);
        if (limited is null)
            return new(NpcShopRejection.ShopUnavailable, []);
        var result = new List<NpcShopPurchaseCount>();
        foreach (var item in limited)
        {
            var bought = await purchases.ReadAsync(Key(session, npcId, item.ItemId, item.Limit!, now), cancellationToken)
                .ConfigureAwait(false);
            if (bought > 0)
                result.Add(new(item.ItemId, bought));
        }
        return new(NpcShopRejection.None, result);
    }

    private NpcShopPurchaseKey Key(SelectedSessionIdentity session, int npcId, int itemId,
        NpcShopPurchaseLimit limit, DateTimeOffset now) =>
        new(limit.Scope == NpcShopLimitScope.Account ? "account" : "character",
            limit.Scope == NpcShopLimitScope.Account ? session.AccountId.Value : session.CharacterId.Value,
            npcId, itemId, rules.PurchasePeriod(limit, now));

    private bool IsCurrent(SelectedSessionIdentity session) =>
        sessions.Find(session.AccountId) == session
        && onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
        && online is { State: OnlineUserState.CharacterActive }
        && online.AccountId == session.AccountId && online.CurrentCharacterId == session.CharacterId;
}
