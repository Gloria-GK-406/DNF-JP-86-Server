using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Accounts;
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
    IAccountWalletDomainService wallets,
    ICharacterBusinessInventoryDomainService inventories,
    IItemPlacementRuleService placements,
    INpcShopRuleService rules,
    INpcShopPurchaseTable purchases, IItemLifecycleDomainService lifecycle, IItemGrantDomainService grants, Dfo.Server.Abstractions.Effects.IRequestReplayProjectionDomainService replay,
    Dfo.Server.Abstractions.World.BossTower.IBossTowerDomainService? bossTower = null, IDungeonParticipationDomainService? participation = null) : INpcShopDomainService
{
    public async ValueTask<NpcShopPurchaseResult> PurchaseAsync(
        SelectedSessionIdentity session, int itemId, int rawCount, int shopId, int npcId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!IsCurrent(session))
            return NpcShopPurchaseResult.Refused(NpcShopRejection.NoSession);
        var inBossTower = bossTower?.Find(session) is not null;
        if (inBossTower ? !bossTower!.CanShop(session, npcId, shopId)
            : participation?.Find(session.CharacterId) is not null
                || townAreas.Find(session.CharacterId) is not { } member || !session.Owns(member))
            return NpcShopPurchaseResult.Refused(NpcShopRejection.ShopUnavailable);
        var character = characters.Get(session.CharacterId);
        var identity = await character.IdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity is null)
            return NpcShopPurchaseResult.Refused(NpcShopRejection.NoSession);

        var priced = await rules.QuoteAsync(npcId, shopId, identity.Job, itemId, rawCount, cancellationToken)
            .ConfigureAwait(false);
        if (priced.Quote is not { } quote)
            return NpcShopPurchaseResult.Refused(priced.Rejection);

        var spaces = new HashSet<BusinessInventorySpace> { quote.Placement.Destination.Space };
        if (quote.Placement.Destination.Space != BusinessInventorySpace.SharedMaterial && ItemSlotRuleService.AllowsQuickSlot(quote.Placement))
            spaces.Add(BusinessInventorySpace.QuickSlot);
        foreach (var cost in quote.Materials)
        {
            var material = await placements.PlacementForAsync(cost.ItemId, cancellationToken).ConfigureAwait(false);
            if (material is null) return NpcShopPurchaseResult.Refused(NpcShopRejection.UnsupportedItem);
            spaces.Add(material.Destination.Space);
            if (material.Destination.Space != BusinessInventorySpace.SharedMaterial && ItemSlotRuleService.AllowsQuickSlot(material))
                spaces.Add(BusinessInventorySpace.QuickSlot);
        }
        var bags = new Dictionary<BusinessInventorySpace, IBusinessItemContainer>();
        var held = new Dictionary<BusinessInventorySpace, IReadOnlyList<ItemReference>>();
        foreach (var space in spaces)
        {
            var bag = inventories.GetStore(session.CharacterId, BusinessInventoryOwnership.StoreOf(space))
                .FindContainer(space);
            if (bag is null) return NpcShopPurchaseResult.Refused(NpcShopRejection.NoRoom);
            bags.Add(space, bag);
            held.Add(space, await bag.GetAllAsync(cancellationToken).ConfigureAwait(false));
        }

        var boundedPlacement = InventoryCapacityPlacement.Limit(quote.Placement,
            await bags[quote.Placement.Destination.Space].OpenSlotCountAsync(cancellationToken).ConfigureAwait(false));
        if (boundedPlacement is null) return NpcShopPurchaseResult.Refused(NpcShopRejection.NoRoom);
        quote = quote with { Placement = boundedPlacement };
        NpcShopPurchaseKey? purchaseKey = null;
        NpcShopPurchaseCount? purchaseCount = null;
        if (quote.Definition.Limit is { } limit)
        {
            purchaseKey = Key(session, npcId, itemId, limit, now);
            var bought = await purchases.ReadAsync(purchaseKey, cancellationToken).ConfigureAwait(false);
            if (quote.Count > limit.Count - bought)
                return NpcShopPurchaseResult.Refused(NpcShopRejection.PurchaseLimitReached);
            purchaseCount = new(itemId, checked(bought + quote.Count));
        }

        var gold = await character.GoldAsync(cancellationToken).ConfigureAwait(false);
        var planned = rules.Plan(quote, held, gold, Dfo.Server.Application.Inventory.EquipmentQualitySeeds.NewRandom());
        if (planned.Plan is not { } plan)
            return NpcShopPurchaseResult.Refused(planned.Rejection);

        var skillPoints = await character.RemainingSkillPointsAsync(cancellationToken).ConfigureAwait(false);
        if (skillPoints is null)
            return NpcShopPurchaseResult.Refused(NpcShopRejection.NoSession);
        var wallet = await wallets.ReadAsync(session.AccountId, cancellationToken).ConfigureAwait(false);
        if (wallet is null)
            return NpcShopPurchaseResult.Refused(NpcShopRejection.NoSession);
        var cera = checked((int)wallet.Cera);

        replay.RecordInventorySlots(session, plan.MaterialChanges.Select(x => x.Slot).ToArray());
        if (quote.GoldCost > 0)
            gold = await character.TrySpendGoldAsync(quote.GoldCost, cancellationToken).ConfigureAwait(false)
                ?? throw new NpcShopPurchaseRejectedException(NpcShopRejection.InsufficientGold);
        foreach (var change in plan.MaterialChanges)
        {
            var bag = bags[change.Slot.Space];
            var existing = await bag.GetAsync(change.Slot.Index, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Shop material disappeared after planning.");
            await ItemContainerOperations.ConsumeAsync(bag, existing, existing.Count - (change.Core?.Count ?? 0), lifecycle, cancellationToken).ConfigureAwait(false);
        }
        foreach (var grant in plan.GrantedSlots)
        {
            var bag = bags[grant.Slot.Space];
            await grants.ApplyPlannedAsync(bag, grant.Slot.Index, grant.Core, cancellationToken).ConfigureAwait(false);
        }
        if (purchaseKey is not null
            && !await purchases.TryAddAsync(purchaseKey, quote.Count, quote.Definition.Limit!.Count, cancellationToken)
                .ConfigureAwait(false))
            throw new NpcShopPurchaseRejectedException(NpcShopRejection.PurchaseLimitReached);

        return new(NpcShopRejection.None, new(session, itemId, quote.Count, gold, skillPoints.Page0Sp,
            cera, quote.GoldCost > 0, plan.MaterialChanges, plan.GrantedSlots, now) { PurchaseCount = purchaseCount });
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
