using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public enum InventorySortOrder { Default, Rarity, Level, Type, Expiration }
public sealed record InventorySortKeys(int Rarity, int Level, int Part, string StackableType);
public interface IInventorySortKeySource
{
    ValueTask<InventorySortKeys> GetAsync(int itemId, CancellationToken ct);
}
/// <summary>Which spaces sort, and the order the archive's sort keys put a space's items in.</summary>
public interface IInventorySortRuleService
{
    bool IsSortable(BusinessInventorySpace space);

    ValueTask<IReadOnlyList<ItemReference>> OrderAsync(BusinessInventorySpace space, InventorySortOrder order,
        IReadOnlyList<ItemReference> movable, CancellationToken ct);
}
public enum InventorySortLockAction { Toggle, Unlock }

public interface IInventorySortDomainService
{
    /// <summary>Returns the new lock state, or null for an unavailable target.</summary>
    ValueTask<bool?> ChangeLockAsync(CharacterId character, BusinessSlot slot,
        InventorySortLockAction action, CancellationToken ct);

    ValueTask<bool> SortAsync(CharacterId character, BusinessInventorySpace space,
        InventorySortOrder order, CancellationToken ct);
}
