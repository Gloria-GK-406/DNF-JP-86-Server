using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemSaleRuleService(IItemSaleDefinitionSource prices, INpcShopDefinitionSource shops)
    : IItemSaleRuleService
{
    public async ValueTask<bool> HasShopAsync(int npcId, CancellationToken cancellationToken) =>
        await shops.GetShopAsync(npcId, cancellationToken).ConfigureAwait(false) is not null;

    public async ValueTask<bool> CanAccessShopAsync(int npcId, bool inTown, bool dungeonCleared,
        CancellationToken cancellationToken)
    {
        var shop = await shops.GetShopAsync(npcId, cancellationToken).ConfigureAwait(false);
        return shop is not null && (shop.VisibleOnDungeonClear ? dungeonCleared : inTown);
    }

    public async ValueTask<ItemSaleQuote> QuoteAsync(ItemReference item, int count, CancellationToken cancellationToken)
    {
        if (count <= 0 || count > short.MaxValue || count > item.Count)
            return new(ItemSaleRejection.InvalidRequest);
        if (item.Snapshot.EquipmentLockId != 0)
            return new(ItemSaleRejection.Locked);
        if ((!item.IsPile && !item.Type.HasFlag(ItemTraits.Equipment))
            || item.Snapshot.ExpireTime != 0)
            return new(ItemSaleRejection.CannotSell);
        var definition = await prices.GetAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        if (definition is not { CanSell: true, Value: >= 0, RatePerThousand: >= 0 })
            return new(ItemSaleRejection.CannotSell);
        if (definition.IsEquipment != ((!item.IsPile && item.Type.HasFlag(ItemTraits.Equipment))))
            return new(ItemSaleRejection.CannotSell);
        // Special equipment can select other client price branches. Keep it intact until those
        // mappings are verified; the ordinary equipment path is backed by the A21 live probe.
        if (definition.IsEquipment && (item.Snapshot.Attribute != 0 || item.Snapshot.SealFlag != 0
            || item.Snapshot.AmplifyType != 0 || item.Snapshot.GenuineUpgrade != 0
            || item.Snapshot.EmancipateEquipmentLevel != 0 || item.Snapshot.RandomOptionState != 0
            || item.Snapshot.RandomOption0.Type != 0 || item.Snapshot.RandomOption1.Type != 0
            || item.Snapshot.RandomOption2.Type != 0))
            return new(ItemSaleRejection.CannotSell);

        // Match A21 RVA 02538230: float32 arithmetic, truncate, then equipment's 90/100.
        // Rounding the combined percentage or multiplying the pile before truncation differs.
        float rawGold;
        if (definition.IsEquipment && definition.MaxDurability != 0)
            rawGold = (float)definition.RatePerThousand * definition.Value * 0.001f
                * Math.Min(item.Snapshot.Durability, definition.MaxDurability) / definition.MaxDurability;
        else
            rawGold = definition.RatePerThousand * 0.001f * definition.Value;
        if (!float.IsFinite(rawGold) || (double)rawGold > int.MaxValue)
            return new(ItemSaleRejection.GoldLimit);
        var unitGold = (long)rawGold;
        if (definition.IsEquipment)
            unitGold = unitGold * 90 / 100;
        return unitGold > int.MaxValue / count
            ? new(ItemSaleRejection.GoldLimit)
            : new(ItemSaleRejection.None, count, (int)(unitGold * count));
    }
}
