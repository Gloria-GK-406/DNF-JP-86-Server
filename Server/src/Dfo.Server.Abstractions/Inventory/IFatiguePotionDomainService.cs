using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IFatiguePotionDomainService
{
    ValueTask<OpeningInventoryReceipt> UseAsync(SelectedSessionIdentity session, int sourceSlot,
        DateTimeOffset now, CancellationToken token);
}
