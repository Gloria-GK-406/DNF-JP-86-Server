using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record ItemSaleQuote(ItemSaleRejection Rejection, int Count = 0, int Gold = 0,
    string? PricingFailure = null);

public interface IItemSaleRuleService
{
    ValueTask<ItemSaleQuote> QuoteAsync(ItemReference item, int count, CancellationToken cancellationToken);
}
