using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Content;

/// <summary>Static item presentation for administration tooltips; null for an ID no item list declares.</summary>
public interface IItemTooltipSource
{
    ValueTask<ItemTooltip?> FindAsync(int itemId, CancellationToken cancellationToken);
}
