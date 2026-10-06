using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Initialization;
namespace Dfo.Server.Abstractions.Administration;
public interface IGmInitializationDomainService
{
    ValueTask<InitializationView> ReadAsync(string secret, CancellationToken ct);
    ValueTask<object> SaveAsync(string secret, SaveInitializationRequest<AccountInitialization>? account, SaveInitializationRequest<CharacterInitialization>? character, CancellationToken ct);
    ValueTask<ItemCatalogPage> SearchAsync(string secret, ItemCatalogFilter filter, CancellationToken ct);
    ValueTask<ItemCatalogDetails> ItemAsync(string secret, int itemId, CancellationToken ct);
}
