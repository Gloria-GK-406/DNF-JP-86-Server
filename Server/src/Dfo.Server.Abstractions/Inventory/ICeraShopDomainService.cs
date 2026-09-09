using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface ICeraShopDomainService
{
    ValueTask<CeraShopPurchaseResult> PurchaseAsync(SelectedSessionIdentity session, CeraShopPurchaseInput input,
        DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class CeraShopPurchaseRejectedException(CeraShopRejection reason, string detail) : Exception(detail)
{
    public CeraShopRejection Reason { get; } = reason;
}
