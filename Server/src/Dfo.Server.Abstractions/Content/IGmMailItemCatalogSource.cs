using Dfo.Server.Data.Administration;

namespace Dfo.Server.Abstractions.Content;

public interface IGmMailItemCatalogSource
{
    ValueTask<IReadOnlyList<GmMailCatalogEntry>> SearchAsync(GmMailItemFilter filter, CancellationToken token);
    ValueTask<GmMailCatalogEntry?> FindAsync(int itemId, CancellationToken token);
}
