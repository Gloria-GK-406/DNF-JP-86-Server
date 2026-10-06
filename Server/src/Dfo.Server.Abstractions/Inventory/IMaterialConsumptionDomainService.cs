using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record MaterialCost(BusinessSlot Slot, int ItemId, int Count);
public sealed record MaterialDebit(MaterialCost Cost, InventoryItemCoreState? Remaining);
public sealed record MaterialConsumptionResult(string? Rejection, IReadOnlyList<MaterialDebit> Debits)
{
    public bool Success => Rejection is null;
}
public interface IMaterialConsumptionDomainService
{
    ValueTask<MaterialConsumptionResult> ConsumeAsync(SelectedSessionIdentity session,
        IReadOnlyList<MaterialCost> costs, CancellationToken cancellationToken);
}
