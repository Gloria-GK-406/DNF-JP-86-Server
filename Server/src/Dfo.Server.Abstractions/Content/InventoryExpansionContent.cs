using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Content;

public enum InventoryExpansionKind { PersonalCargo, AccountCargo, Main, Avatar }
public sealed record InventoryExpansionGrant(InventoryExpansionKind Kind, int Parameter, int ProductId = 0);
public sealed record AccountCargoTier(int Capacity, int GoldLimit, int MaterialId, int MaterialCount, int GoldCost, int ProductId);
public sealed record AccountCargoDefinition(int RequiredLevel, IReadOnlyList<AccountCargoTier> Tiers);
public interface IInventoryExpansionDefinitionSource
{
    ValueTask<AccountCargoDefinition> AccountCargoAsync(CancellationToken ct);
    ValueTask<InventoryExpansionGrant?> ItemAsync(int itemId, CancellationToken ct);
    ValueTask<InventoryExpansionGrant?> ProductAsync(int productId, int itemId, CancellationToken ct);
}
