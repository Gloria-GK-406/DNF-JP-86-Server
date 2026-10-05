using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IDailyRefillConfigurationRuleService : IItemCatalogSelectionRuleService
{
    ValueTask<IReadOnlyList<DailyRefillRule>> DeclaredPvfAsync(DateTimeOffset at, CancellationToken ct);
    ValueTask<IReadOnlyList<DailyRefillRule>> ActivePvfAsync(DateTimeOffset at, CancellationToken ct);
    ValueTask<string> ItemNameAsync(int itemId, CancellationToken ct);
    ValueTask<DailyRefillRule> ResolveCustomAsync(CustomDailyRefill input, CancellationToken ct);
    ValueTask<DailyRefillRule?> TryResolveCustomAsync(CustomDailyRefill input, CancellationToken ct);
}
