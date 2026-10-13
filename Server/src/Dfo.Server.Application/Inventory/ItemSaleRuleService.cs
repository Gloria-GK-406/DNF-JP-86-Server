using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemSaleRuleService(IItemSaleDefinitionSource prices)
    : IItemSaleRuleService
{
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
        if (definition is { CanSell: false })
            return new(ItemSaleRejection.CannotSell);
        if (definition is null)
            return new(ItemSaleRejection.None, count, 0, "Missing sale price definition.");
        if (definition.PricingFailure is { } failure)
            return new(ItemSaleRejection.None, count, 0, failure);
        if (definition.Value < 0 || definition.RatePerThousand < 0)
            return new(ItemSaleRejection.None, count, 0, "Negative sale value or rate.");
        if (definition.IsEquipment != ((!item.IsPile && item.Type.HasFlag(ItemTraits.Equipment))))
            return new(ItemSaleRejection.None, count, 0, "Sale definition and item equipment type differ.");

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
