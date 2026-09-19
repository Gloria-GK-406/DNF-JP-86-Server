using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IItemSaleDomainService
{
    ValueTask<ItemSaleResult> SellAsync(SelectedSessionIdentity session, BusinessSlot slot,
        int count, int npcId, DateTimeOffset now, CancellationToken cancellationToken);
}

// This exception must cross the command transaction boundary before becoming a protocol refusal.
public sealed class ItemSaleRejectedException(ItemSaleRejection reason) : Exception($"Item sale refused: {reason}")
{
    public ItemSaleRejection Reason { get; } = reason;
}
