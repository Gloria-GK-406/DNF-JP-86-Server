using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class NpcShopRuleService(
    INpcShopDefinitionSource content,
    IItemPlacementRuleService placementRules,
    IItemArrivalRuleService arrivals,
    IDailyRefillRuleService days) : INpcShopRuleService
{
    public async ValueTask<NpcShopQuoteResult> QuoteAsync(
        int npcId, int shopId, byte job, int itemId, int rawCount, CancellationToken cancellationToken)
    {
        var shop = await content.GetShopAsync(npcId, cancellationToken).ConfigureAwait(false);
        if (shop is null || shop.ShopId != shopId)
            return new(NpcShopRejection.ShopUnavailable, null);
        if (!OfferedItems(shop, job).Contains(itemId))
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

    public async ValueTask<IReadOnlyList<NpcShopItemDefinition>?> LimitedItemsAsync(
        int npcId, byte job, CancellationToken cancellationToken)
    {
        var shop = await content.GetShopAsync(npcId, cancellationToken).ConfigureAwait(false);
        if (shop is null)
            return null;
        var result = new List<NpcShopItemDefinition>();
        foreach (var itemId in OfferedItems(shop, job))
            if (await content.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false)
                is { IsReadable: true, Limit: not null } item)
                result.Add(item);
        return result;
    }

    public NpcShopPlanningResult Plan(
        NpcShopQuote quote, IReadOnlyList<ContainerSlotItem> held, int gold, int itemIdentitySeed)
    {
        if (gold < quote.GoldCost)
            return new(NpcShopRejection.InsufficientGold, null);
        var standing = held.ToDictionary(item => item.SlotIndex);
        var materialChanges = new List<NpcShopSlotChange>();
        foreach (var cost in quote.Materials)
        {
            var available = standing.Values.Where(item => item.ItemId == cost.ItemId && item.IsPile)
                .OrderBy(item => item.SlotIndex).ToArray();
            if (available.Sum(item => (long)item.Count) < cost.Count)
                return new(NpcShopRejection.InsufficientMaterials, null);
            var remaining = cost.Count;
            foreach (var pile in available)
            {
                if (remaining == 0)
                    break;
                var take = Math.Min(remaining, pile.Count);
                remaining -= take;
                var after = pile.Count == take ? null : pile.Core with { Value = pile.Count - take };
                materialChanges.Add(new(pile.SlotIndex, after));
                if (after is null)
                    standing.Remove(pile.SlotIndex);
                else
                    standing[pile.SlotIndex] = pile with { Core = after };
            }
        }

        var grants = new List<NpcShopGrantedSlot>();
        var outstanding = quote.Count;
        while (outstanding > 0)
        {
            var placement = quote.Placement;
            var arrival = arrivals.ForGrant(
                standing.Values.Where(item => item.ItemId == quote.Definition.ItemId && item.IsPile)
                    .Select(item => new ItemPile(item.SlotIndex, item.Count)).ToArray(),
                outstanding, placement, standing.Keys.ToHashSet());
            int slot;
            int added;
            InventoryItemCoreState core;
            switch (arrival)
            {
                case ItemArrival.OntoExistingPile pile:
                    slot = pile.SlotIndex;
                    added = pile.Added;
                    core = standing[slot].Core with { Value = pile.Total };
                    break;
                case ItemArrival.IntoEmptySlot empty:
                    slot = empty.SlotIndex;
                    added = empty.Count;
                    core = StackableItemCore.Create(placement.ItemKind, quote.Definition.ItemId, added);
                    if (placement.WorldClass == ItemWorldClass.Equipment)
                        core = core with
                        {
                            Value = itemIdentitySeed,
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
            standing[slot] = new(slot, core, placement.WorldClass);
            grants.Add(new(new InventoryItemState(null, slot, core), added));
            outstanding -= added;
        }
        return new(NpcShopRejection.None, new(materialChanges, grants));
    }

    public string PurchasePeriod(NpcShopPurchaseLimit limit, DateTimeOffset now) =>
        limit.ResetsDaily ? days.PeriodOf(now) : "lifetime";

    private static IEnumerable<int> OfferedItems(NpcShopDefinition shop, byte job) =>
        shop.Pages.Where(page => page.Job is null || page.Job == job).SelectMany(page => page.ItemIds).Distinct();
}
