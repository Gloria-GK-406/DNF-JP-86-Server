using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>The one reader of what entry grants an inventory.</summary>
internal sealed class InventoryLifecycleRuleService(
    IEntryLifecycleContentSource content) : IInventoryLifecycleRuleService
{
    public ValueTask<IReadOnlyList<DailyRefillRule>> DailyRefillsAsync(
        DateTimeOffset preparedAt, CancellationToken cancellationToken) =>
        content.GetDailyRefillRulesAsync(preparedAt, cancellationToken);
}
