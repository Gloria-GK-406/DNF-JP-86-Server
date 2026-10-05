using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Content;

public interface IItemCatalogSource
{
    ValueTask<IReadOnlyList<ItemCatalogEntry>> SearchAsync(ItemCatalogFilter filter, CancellationToken token);
    ValueTask<ItemCatalogEntry?> FindAsync(int itemId, CancellationToken token);
}
