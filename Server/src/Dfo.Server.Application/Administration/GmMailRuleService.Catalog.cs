using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

internal sealed partial class GmMailRuleService
{
    public async ValueTask<GmMailCatalogPage> SearchItemsAsync(GmMailItemFilter filter, CancellationToken token)
    {
        if (filter.Page is < 1 or > 10000 || filter.PageSize is < 1 or > 50 || filter.ItemId <= 0
            || filter.Q?.Length > 128 || filter.EquipmentType?.Length > 64
            || filter.Category is not (null or "equipment" or "material" or "consumable" or "quest" or "other")
            || filter.MinimumLevel < 0 || filter.MaximumLevel < 0 || filter.MinimumLevel > filter.MaximumLevel
            || filter.Rarity is < 0 or > 6)
            throw Invalid("物品查询参数无效。");
        var candidates = await catalog.SearchAsync(filter, token);
        var rows = new List<GmMailCatalogEntry>();
        var skip = (filter.Page - 1) * filter.PageSize;
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var prepared = await PrepareItemsAsync(new("", "", [], [new(candidate.ItemId, 1)], 0), clock.GetUtcNow(), token);
                // Apply admission before pagination so restricted pickers do not produce empty pages.
                if (filter.StackableOnly && prepared.Single().Preview.Kind != "stackable") continue;
            }
            catch (GmMailException error) when (error.Reason == GmMailRefusal.Invalid) { continue; }
            if (skip-- > 0) continue;
            if (rows.Count == filter.PageSize) return new(rows, filter.Page, filter.PageSize, true);
            rows.Add(candidate);
        }
        return new(rows, filter.Page, filter.PageSize, false);
    }

    public async ValueTask<GmMailItemDetails> ItemDetailsAsync(int itemId, CancellationToken token)
    {
        if (itemId <= 0) throw Invalid("物品 ID 必须为正整数。");
        var entry = await catalog.FindAsync(itemId, token)
            ?? throw new GmMailException(GmMailRefusal.NotFound, "未找到该物品。");
        var prepared = (await PrepareItemsAsync(new("", "", [], [new(itemId, 1)], 0), clock.GetUtcNow(), token)).Single();
        var options = prepared.Preview.Kind == "equipment" ? await equipmentRules.CapabilitiesAsync(prepared.Snapshot, token) : null;
        return new(entry, prepared.Preview.Kind, prepared.Preview.Sealed, prepared.Preview.Durability, options);
    }
}
