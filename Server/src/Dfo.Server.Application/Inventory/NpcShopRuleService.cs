using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class NpcShopRuleService(
    INpcShopDefinitionSource content,
    IItemPlacementRuleService placementRules,
    IItemArrivalRuleService arrivals,
    IDailyRefillRuleService days, IItemExpirationRuleService expiration, TimeProvider clock) : INpcShopRuleService
{
    public static GuildShopAccess? AccessFor(int npcId, int? itemId)
    {
        var extensionTicket = itemId == GuildDungeonExtensionTicket.ItemId;
        if (extensionTicket && npcId != GuildHallTown.DungeonSupplyNpcId)
            return null;
        return new(GuildHallTown.ShopArea(npcId), extensionTicket, npcId == GuildHallTown.DungeonMedalNpcId);
    }

    internal readonly record struct GuildShopAccess(byte? RequiredArea, bool RequiresLeader, bool UsesMedalShop)
    {
        public bool AllowsMember(GuildMember? member) =>
            RequiredArea is null || member is not null && (!RequiresLeader || member.IsLeader);

        public bool AllowsVariant(int? requested, int? active) =>
            !UsesMedalShop || active.HasValue && (!requested.HasValue || requested == active);
    }

    public async ValueTask<NpcShopQuoteResult> QuoteAsync(
        int npcId, int shopId, byte job, int itemId, int rawCount, CancellationToken cancellationToken,
        int? activeVariant = null, NpcShopExpertJob expertJob = default)
    {
        if (activeVariant is { } variant && shopId != variant)
            return new(NpcShopRejection.ShopUnavailable, null);
        var shop = await ReadShopAsync(npcId, activeVariant, cancellationToken).ConfigureAwait(false);
        // Purchase currently admits town shops only; clear-room shops require a separate run authorization.
        if (shop is null || shop.ShopId != shopId || shop.VisibleOnDungeonClear)
            return new(NpcShopRejection.ShopUnavailable, null);
        if (!OfferedItems(shop, job, expertJob).Contains(itemId))
            return new(NpcShopRejection.ItemNotSold, null);
        var definition = await content.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false);
        var placement = await placementRules.PlacementForAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (definition is not { IsReadable: true } || placement is null || placement.ItemKind == 0
            || placement.WorldClass is not (ItemWorldClass.Equipment or ItemWorldClass.Interchangeable)
            || definition.GoldPrice is null && definition.Materials.Count == 0)
            return new(NpcShopRejection.UnsupportedItem, null);

        // A21 sends zero for a single non-stackable item. Negative counts have no observed meaning.
        var count = rawCount == 0 && placement.WorldClass == ItemWorldClass.Equipment ? 1 : rawCount;
        if (count is <= 0 or > short.MaxValue || placement.WorldClass == ItemWorldClass.Equipment && count != 1)
            return new(NpcShopRejection.InvalidCount, null);

        var unitGold = definition.GoldPrice is { } price ? Math.Max(0L, (long)price + definition.AddPrice) : 0L;
        var totalGold = unitGold * count;
        var materials = new List<NpcShopMaterialCost>();
        foreach (var group in definition.Materials.GroupBy(cost => cost.ItemId))
        {
            var cost = group.Sum(value => (long)value.Count) * count;
            if (group.Key <= 0 || cost is <= 0 or > int.MaxValue)
                return new(NpcShopRejection.UnsupportedItem, null);
            materials.Add(new(group.Key, (int)cost));
        }
        if (totalGold > int.MaxValue)
            return new(NpcShopRejection.InvalidCount, null);
        return new(NpcShopRejection.None, new(definition, placement, count, (int)totalGold, materials));
    }

    public async ValueTask<NpcShopQuoteResult> QuoteSecretAsync(
        DungeonSecretShopItem item, int rawCount, CancellationToken cancellationToken)
    {
        // The reference treats zero as a single purchase. The A21 wire shape is known,
        // while its zero-quantity cases still need a client capture.
        var count = rawCount == 0 ? 1 : rawCount;
        if (count is <= 0 or > short.MaxValue)
            return new(NpcShopRejection.InvalidCount, null);
        if (count > item.RemainingCount)
            return new(NpcShopRejection.PurchaseLimitReached, null);

        var definition = await content.GetItemAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        var placement = await placementRules.PlacementForAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        if (definition is null || placement is null || placement.ItemKind == 0
            || placement.WorldClass is not (ItemWorldClass.Equipment or ItemWorldClass.Interchangeable))
            return new(NpcShopRejection.UnsupportedItem, null);
        if (placement.WorldClass == ItemWorldClass.Equipment && count != 1)
            return new(NpcShopRejection.InvalidCount, null);

        var total = (long)item.UnitPrice * count;
        if (total is < 0 or > int.MaxValue)
            return new(NpcShopRejection.InvalidCount, null);

        // The rolled offer defines the price, currency and limit. Item content supplies grant attributes.
        var grant = definition with { Limit = null };
        return item.CostItemId == 0
            ? new(NpcShopRejection.None, new(grant, placement, count, (int)total, []))
            : new(NpcShopRejection.None, new(grant, placement, count, 0, [new(item.CostItemId, (int)total)]));
    }

    public async ValueTask<IReadOnlyList<NpcShopItemDefinition>?> LimitedItemsAsync(
        int npcId, byte job, CancellationToken cancellationToken, int? activeVariant = null, NpcShopExpertJob expertJob = default)
    {
        var shop = await ReadShopAsync(npcId, activeVariant, cancellationToken).ConfigureAwait(false);
        if (shop is null)
            return null;
        var result = new List<NpcShopItemDefinition>();
        foreach (var itemId in OfferedItems(shop, job, expertJob))
            if (await content.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false)
                is { IsReadable: true, Limit: not null } item)
                result.Add(item);
        return result;
    }

    private ValueTask<NpcShopDefinition?> ReadShopAsync(int npcId, int? activeVariant, CancellationToken ct) =>
        activeVariant is { } id ? content.GetShopVariantAsync(npcId, id, ct) : content.GetShopAsync(npcId, ct);

    public NpcShopPlanningResult Plan(
        NpcShopQuote quote,
        IReadOnlyDictionary<BusinessInventorySpace, IReadOnlyList<ItemReference>> held,
        int gold, int itemIdentitySeed)
    {
        if (gold < quote.GoldCost)
            return new(NpcShopRejection.InsufficientGold, null);
        var standing = held.SelectMany(group => group.Value.Select(item =>
                (Slot: new BusinessSlot(group.Key, item.SlotIndex), Item: item.Snapshot)))
            .ToDictionary(entry => entry.Slot, entry => entry.Item);
        var materialChanges = new List<NpcShopSlotChange>();
        foreach (var cost in quote.Materials)
        {
            var available = standing.Where(entry => entry.Value.ItemId == cost.ItemId && entry.Value.IsPile)
                .OrderBy(entry => entry.Key.Space).ThenBy(entry => entry.Key.Index).ToArray();
            if (available.Sum(entry => (long)entry.Value.Count) < cost.Count)
                return new(NpcShopRejection.InsufficientMaterials, null);
            var remaining = cost.Count;
            foreach (var (slot, pile) in available)
            {
                if (remaining == 0)
                    break;
                var take = Math.Min(remaining, pile.Count);
                remaining -= take;
                var after = pile.Count == take ? null : pile with { Count = pile.Count - take };
                materialChanges.Add(new(slot, after));
                if (after is null)
                    standing.Remove(slot);
                else
                    standing[slot] = after;
            }
        }

        var grants = new List<NpcShopGrantedSlot>();
        var baseline = expiration.Prepare(new ItemSnapshot { ItemId = quote.Definition.ItemId,
            Type = quote.Placement.Traits, LegacyKind = quote.Placement.ItemKind,
            IsPile = quote.Placement.WorldClass == ItemWorldClass.Interchangeable }, quote.Placement.Lifetime, clock.GetUtcNow());
        var outstanding = quote.Count;
        while (outstanding > 0)
        {
            var placement = quote.Placement;
            var candidates = CandidateSlots(placement).ToArray();
            var arrival = arrivals.ForGrant(
                candidates
                    .Where(slot => standing.TryGetValue(slot, out var item)
                        && item.CanStackWith(baseline))
                    .Select(slot => new ItemPile(slot, standing[slot].Count)).ToArray(),
                outstanding, placement,
                candidates.Where(standing.ContainsKey).ToHashSet());
            BusinessSlot slot;
            int added;
            ItemSnapshot core;
            switch (arrival)
            {
                case ItemArrival.OntoExistingPile pile:
                    slot = pile.Slot;
                    added = pile.Added;
                    core = standing[slot] with { Count = pile.Total };
                    break;
                case ItemArrival.IntoEmptySlot empty:
                    slot = empty.Slot;
                    added = empty.Count;
                    core = baseline with { Count = placement.WorldClass == ItemWorldClass.Interchangeable ? added : 1 };
                    if (placement.WorldClass == ItemWorldClass.Equipment)
                        core = core with
                        {
                            InstanceValue = itemIdentitySeed,
                            Durability = quote.Definition.Durability,
                            SealFlag = quote.Definition.Sealed ? (byte)1 : (byte)0,
                        };
                    break;
                case ItemArrival.NoRoom:
                    return new(NpcShopRejection.NoRoom, null);
                default:
                    throw new InvalidOperationException("An unrecognized shop arrival was planned.");
            }
            if (added <= 0 || added > outstanding)
                throw new InvalidOperationException("A shop arrival did not consume a positive bounded quantity.");
            standing[slot] = core;
            grants.Add(new(slot, core, added));
            outstanding -= added;
        }
        return new(NpcShopRejection.None, new(materialChanges, grants));
    }

    public string PurchasePeriod(NpcShopPurchaseLimit limit, DateTimeOffset now) =>
        limit.ResetsDaily ? days.PeriodOf(now) : "lifetime";

    private static IEnumerable<int> OfferedItems(NpcShopDefinition shop, byte job, NpcShopExpertJob expertJob) =>
        shop.Pages.Where(page => (page.Job is null || page.Job == job)
            && (page.ExpertJob is null || page.ExpertJob == expertJob.Type && page.MinimumExpertJobLevel <= expertJob.Level))
            .SelectMany(page => page.ItemIds).Distinct();

    private static IEnumerable<BusinessSlot> CandidateSlots(ItemPlacement placement)
    {
        var own = Enumerable.Range(
                placement.Destination.FirstIndex,
                placement.Destination.LastIndex - placement.Destination.FirstIndex + 1)
            .Select(index => new BusinessSlot(placement.Destination.Space, index));
        return placement.Destination.Space != BusinessInventorySpace.SharedMaterial && ItemSlotRuleService.AllowsQuickSlot(placement)
            ? own.Concat(Enumerable.Range(0, 6)
                .Select(index => new BusinessSlot(BusinessInventorySpace.QuickSlot, index)))
            : own;
    }
}
