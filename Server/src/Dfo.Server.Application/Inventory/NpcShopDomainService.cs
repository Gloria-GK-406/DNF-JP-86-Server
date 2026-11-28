using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class NpcShopDomainService(
    ISelectedSessionDomainService sessions,
    IOnlineCharacterRuntimeDomainService onlineCharacters,
    ICharacterDomainService characters,
    IAccountWalletDomainService wallets,
    ICharacterBusinessInventoryDomainService inventories,
    IItemPlacementRuleService placements,
    INpcShopRuleService rules,
    INpcShopPurchaseTable purchases, IItemLifecycleDomainService lifecycle, IItemGrantDomainService grants,
    Dfo.Server.Abstractions.World.BossTower.IBossTowerDomainService bossTower, IDungeonParticipationDomainService participation,
    IGuildHallAccessDomainService halls, IGuildDungeonDomainService guildDungeons,
    IExpertJobQueryDomainService expertJobs, IDungeonRunSettlementDomainService dungeonRuns) : INpcShopDomainService
{
    public async ValueTask<NpcShopPurchaseResult> PurchaseAsync(
        SelectedSessionIdentity session, int itemId, int rawCount, int shopId, int npcId,
        DateTimeOffset now, IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
    {
        footprint.DemandActive(session);
        if (!sessions.IsInWorld(session))
            return NpcShopPurchaseResult.Refused(NpcShopRejection.NoSession);
        var inBossTower = bossTower.Find(session) is not null;
        if (inBossTower ? !bossTower.CanShop(session, npcId, shopId)
            : participation.Find(session.CharacterId) is not null
                || onlineCharacters.StandingOf(session) is null)
            return NpcShopPurchaseResult.Refused(NpcShopRejection.ShopUnavailable);
        var access = await GuildAccessAsync(session, npcId, itemId, shopId, now, cancellationToken).ConfigureAwait(false);
        if (!access.Allowed)
            return NpcShopPurchaseResult.Refused(NpcShopRejection.ShopUnavailable);
        var character = characters.Get(session.CharacterId);
        var identity = await character.IdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity is null)
            return NpcShopPurchaseResult.Refused(NpcShopRejection.NoSession);

        var expertJob = await ReadExpertJobAsync(session.CharacterId, cancellationToken).ConfigureAwait(false);
        var priced = await rules.QuoteAsync(npcId, shopId, identity.Job, itemId, rawCount, cancellationToken, access.ActiveVariant, expertJob)
            .ConfigureAwait(false);
        if (priced.Quote is not { } quote)
            return NpcShopPurchaseResult.Refused(priced.Rejection);

        return await ExecutePurchaseAsync(session, character, npcId, quote, now, footprint, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<SecretShopPurchaseResult> PurchaseSecretAsync(
        SelectedSessionIdentity session, int itemId, int rawCount, DateTimeOffset now,
        IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
    {
        footprint.DemandActive(session);
        if (!sessions.IsInWorld(session))
            return new(NpcShopRejection.NoSession);
        if (dungeonRuns.Find(session.CharacterId) is not { SecretShop: { } offer } run)
            return new(NpcShopRejection.ShopUnavailable);
        if (offer.Items.FirstOrDefault(item => item.ItemId == itemId) is not { } product)
            return new(NpcShopRejection.ItemNotSold);

        var priced = await rules.QuoteSecretAsync(product, rawCount, cancellationToken).ConfigureAwait(false);
        if (priced.Quote is not { } quote)
            return new(priced.Rejection);

        var purchase = await ExecutePurchaseAsync(session, characters.Get(session.CharacterId),
            offer.NpcId, quote, now, footprint, cancellationToken).ConfigureAwait(false);
        if (purchase.Receipt is not { } receipt)
            return new(purchase.Rejection);

        if (!dungeonRuns.TryStageSecretShopPurchase(session.CharacterId, run.RunId, itemId, quote.Count))
            throw new NpcShopPurchaseRejectedException(NpcShopRejection.ShopUnavailable);
        var costItemRemaining = receipt.MaterialChanges.FirstOrDefault()?.Core?.Count ?? 0;
        return new(NpcShopRejection.None, receipt, product.RemainingCount - quote.Count,
            product.CostItemId, costItemRemaining);
    }

    private async ValueTask<NpcShopPurchaseResult> ExecutePurchaseAsync(SelectedSessionIdentity session,
        ICharacter character, int npcId, NpcShopQuote quote, DateTimeOffset now,
        IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
    {
        var itemId = quote.Definition.ItemId;
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

        footprint.RecordInventorySlots(session,
            plan.MaterialChanges.Select(change => change.Slot).Concat(plan.GrantedSlots.Select(grant => grant.Slot)).Distinct().ToArray());
        if (quote.GoldCost > 0)
        {
            footprint.DemandActive(session);
            gold = await character.TrySpendGoldAsync(quote.GoldCost, cancellationToken).ConfigureAwait(false)
                ?? throw new NpcShopPurchaseRejectedException(NpcShopRejection.InsufficientGold);
        }
        foreach (var change in plan.MaterialChanges)
        {
            var bag = bags[change.Slot.Space];
            var existing = await bag.GetAsync(change.Slot.Index, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Shop material disappeared after planning.");
            footprint.DemandActive(session);
            await ItemContainerOperations.ConsumeAsync(bag, existing, existing.Count - (change.Core?.Count ?? 0), lifecycle, cancellationToken).ConfigureAwait(false);
        }
        foreach (var grant in plan.GrantedSlots)
        {
            var bag = bags[grant.Slot.Space];
            footprint.DemandActive(session);
            await grants.ApplyPlannedAsync(bag, grant.Slot.Index, grant.Core, cancellationToken).ConfigureAwait(false);
        }
        footprint.DemandActive(session);
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
        if (!sessions.IsInWorld(session))
            return new(NpcShopRejection.NoSession, []);
        var identity = await characters.Get(session.CharacterId).IdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity is null)
            return new(NpcShopRejection.NoSession, []);
        var access = await GuildAccessAsync(session, npcId, null, null, now, cancellationToken).ConfigureAwait(false);
        if (!access.Allowed)
            return new(NpcShopRejection.ShopUnavailable, []);
        var expertJob = await ReadExpertJobAsync(session.CharacterId, cancellationToken).ConfigureAwait(false);
        var limited = await rules.LimitedItemsAsync(npcId, identity.Job, cancellationToken, access.ActiveVariant, expertJob).ConfigureAwait(false);
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

    private async ValueTask<NpcShopExpertJob> ReadExpertJobAsync(CharacterId owner, CancellationToken ct)
    {
        var state = await expertJobs.ReadAsync(owner, ct).ConfigureAwait(false);
        return await expertJobs.DescribeAsync(state, ct).ConfigureAwait(false) is { } progress
            ? new(state.Type, progress.Level) : default;
    }

    private async ValueTask<(bool Allowed, int? ActiveVariant)> GuildAccessAsync(SelectedSessionIdentity session,
        int npcId, int? itemId, int? shopId, DateTimeOffset now, CancellationToken ct)
    {
        if (NpcShopRuleService.AccessFor(npcId, itemId) is not { } access)
            return (false, null);
        var visit = access.RequiredArea is { } area
            ? await halls.VisitAsync(session, area, ct).ConfigureAwait(false) : null;
        if (!access.AllowsMember(visit?.Membership.Member))
            return (false, null);
        var variant = access.UsesMedalShop && visit is not null
            ? await guildDungeons.ReadMedalShopAsync(visit.Membership.Guild.Id, now, ct).ConfigureAwait(false) : null;
        return (access.AllowsVariant(shopId, variant), variant);
    }

    private NpcShopPurchaseKey Key(SelectedSessionIdentity session, int npcId, int itemId,
        NpcShopPurchaseLimit limit, DateTimeOffset now) =>
        new(limit.Scope == NpcShopLimitScope.Account ? "account" : "character",
            limit.Scope == NpcShopLimitScope.Account ? session.AccountId.Value : session.CharacterId.Value,
            npcId, itemId, rules.PurchasePeriod(limit, now));

}
