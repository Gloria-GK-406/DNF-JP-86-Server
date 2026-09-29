using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public enum InventorySortOrder { Default, Rarity, Level, Type, Expiration }
public sealed record InventorySortKeys(int Rarity, int Level, int Part, string StackableType);
public interface IInventorySortKeySource
{
    ValueTask<InventorySortKeys> GetAsync(int itemId, CancellationToken ct);
}
public interface IInventorySortDomainService
{
    ValueTask<bool> SortAsync(CharacterId character, BusinessInventorySpace space,
        InventorySortOrder order, CancellationToken ct);
}
