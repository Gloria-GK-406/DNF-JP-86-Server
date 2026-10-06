using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface ICargoTransferDomainService
{
    ValueTask<ItemMoveOutcome> MoveAsync(SelectedSessionIdentity session, BusinessSlot source,
        BusinessSlot destination, int count, CancellationToken cancellationToken);
}

public interface ICargoItemRuleService
{
    ValueTask<bool> CanStoreAsync(BusinessInventorySpace destination, ItemReference item,
        DateTimeOffset now, CancellationToken cancellationToken);
}
