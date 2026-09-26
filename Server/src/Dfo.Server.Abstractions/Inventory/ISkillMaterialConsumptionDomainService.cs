using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public abstract record SkillMaterialLocation
{
    public sealed record Account(AccountMaterialKind Kind) : SkillMaterialLocation;
    public sealed record Carried(BusinessSlot Slot) : SkillMaterialLocation;
}
public sealed record SkillMaterialCost(SkillMaterialLocation Location, int ItemId, int Count);
public sealed record SkillMaterialDebit(SkillMaterialCost Cost, InventoryItemCoreState? Remaining,
    AccountMaterialChange? AccountChange);
public sealed record SkillMaterialConsumptionResult(string? Rejection, IReadOnlyList<SkillMaterialDebit> Debits)
{
    public bool Success => Rejection is null;
}
public interface ISkillMaterialConsumptionDomainService
{
    ValueTask<SkillMaterialConsumptionResult> ConsumeAsync(SelectedSessionIdentity session,
        IReadOnlyList<SkillMaterialCost> costs, CancellationToken cancellationToken);
}
