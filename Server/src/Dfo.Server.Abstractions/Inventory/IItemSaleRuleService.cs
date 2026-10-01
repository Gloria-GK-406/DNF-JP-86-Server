using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record ItemSaleQuote(ItemSaleRejection Rejection, int Count = 0, int Gold = 0);

public interface IItemSaleRuleService
{
    ValueTask<bool> HasShopAsync(int npcId, CancellationToken cancellationToken);
    ValueTask<bool> CanAccessShopAsync(int npcId, bool inTown, bool dungeonCleared,
        CancellationToken cancellationToken);
    ValueTask<ItemSaleQuote> QuoteAsync(ContainerSlotItem item, int count, CancellationToken cancellationToken);
}
