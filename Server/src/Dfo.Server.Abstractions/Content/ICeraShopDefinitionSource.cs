using Dfo.Server.Data.Content;

namespace Dfo.Server.Abstractions.Content;

public interface ICeraShopDefinitionSource
{
    ValueTask<CeraShopProductDefinition?> GetProductAsync(int productId, CancellationToken cancellationToken);
    ValueTask<CeraShopItemDefinition?> GetItemAsync(int itemId, CancellationToken cancellationToken);
}
