using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Administration;
namespace Dfo.Server.Abstractions.Administration;

public interface IGmDailyRefillDomainService
{
    ValueTask<DailyRefillList> ListAsync(string secret, CancellationToken ct);
    ValueTask<DailyRefillList> ChangeAsync(string secret, DailyRefillChange change, CancellationToken ct);
    ValueTask<ItemCatalogPage> SearchAsync(string secret, ItemCatalogFilter filter, CancellationToken ct);
    ValueTask<ItemCatalogDetails> ItemAsync(string secret, int id, CancellationToken ct);
}
