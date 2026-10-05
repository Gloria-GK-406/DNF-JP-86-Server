using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class DailyRefillConfigurationRuleService(IEntryLifecycleContentSource content,
    IItemCatalogRuleService catalog, IItemCatalogSource itemNames) : IDailyRefillConfigurationRuleService
{
    public string SelectionKey => "daily-refill";
    public bool Allows(ItemCatalogDefinition definition) => PermanentOrdinaryItemRules.Allows(definition)
        && definition.Placement.WorldClass == ItemWorldClass.Interchangeable
        && PermanentOrdinaryItemRules.IsOrdinaryBackpack(definition.Placement.Destination.Space);

    public ValueTask<IReadOnlyList<DailyRefillRule>> DeclaredPvfAsync(DateTimeOffset at, CancellationToken ct) =>
        content.GetDeclaredDailyRefillRulesAsync(at, ct);

    public ValueTask<IReadOnlyList<DailyRefillRule>> ActivePvfAsync(DateTimeOffset at, CancellationToken ct) =>
        content.GetDailyRefillRulesAsync(at, ct);

    public async ValueTask<string> ItemNameAsync(int itemId, CancellationToken ct) =>
        (await itemNames.FindAsync(itemId, ct))?.Name ?? $"物品 {itemId}";

    public async ValueTask<DailyRefillRule> ResolveCustomAsync(CustomDailyRefill input, CancellationToken ct)
    {
        ItemCatalogDefinition definition;
        try { definition = await catalog.DefinitionAsync(input.ItemId, ct); }
        catch (KeyNotFoundException e) { throw new ArgumentException(e.Message, e); }
        if (!Allows(definition)) throw new ArgumentException("每日补给仅支持永久普通背包堆叠物品。");
        var placement = definition.Placement;
        return new(input.ItemId, input.Quantity, input.ExpiresAt ?? DateTimeOffset.MaxValue, (DailyRefillMode)input.Mode,
            placement.StackLimit, placement.ItemKind, placement.Destination);
    }

    // A changed PVF can invalidate a previously accepted custom item. Skip that rule, not entry.
    public async ValueTask<DailyRefillRule?> TryResolveCustomAsync(CustomDailyRefill input, CancellationToken ct)
    {
        try { return await ResolveCustomAsync(input, ct); }
        catch (ArgumentException) { return null; }
    }
}
