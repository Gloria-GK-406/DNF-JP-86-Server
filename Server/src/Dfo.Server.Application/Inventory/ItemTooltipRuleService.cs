using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemTooltipRuleService(IItemTooltipSource tooltips) : IItemTooltipRuleService
{
    public ValueTask<ItemTooltip?> FindAsync(int itemId, CancellationToken cancellationToken) =>
        tooltips.FindAsync(itemId, cancellationToken);
}
