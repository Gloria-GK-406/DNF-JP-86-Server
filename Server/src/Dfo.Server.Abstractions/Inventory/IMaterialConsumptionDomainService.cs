using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public abstract record MaterialLocation
{
    public sealed record Account(AccountMaterialKind Kind) : MaterialLocation;
    public sealed record Carried(BusinessSlot Slot) : MaterialLocation;
}
public sealed record MaterialCost(MaterialLocation Location, int ItemId, int Count);
public sealed record MaterialDebit(MaterialCost Cost, InventoryItemCoreState? Remaining,
    AccountMaterialChange? AccountChange);
public sealed record MaterialConsumptionResult(string? Rejection, IReadOnlyList<MaterialDebit> Debits)
{
    public bool Success => Rejection is null;
}
public interface IMaterialConsumptionDomainService
{
    ValueTask<MaterialConsumptionResult> ConsumeAsync(SelectedSessionIdentity session,
        IReadOnlyList<MaterialCost> costs, CancellationToken cancellationToken);
}
