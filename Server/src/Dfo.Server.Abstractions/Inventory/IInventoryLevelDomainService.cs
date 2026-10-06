using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.Inventory;

public interface IInventoryLevelRuleService
{
    bool IsValid(InventoryExpansionKind kind, int level, AccountCargoDefinition account);
}
public interface IInventoryLevelDomainService
{
    /// <summary>Levels use the game's persisted capacity parameters, not an ordinal tier number.</summary>
    ValueTask<int> GetLevelAsync(ContainerId container, CancellationToken ct);
    ValueTask SetLevelAsync(ContainerId container, int level, CancellationToken ct, int? expectedLevel = null);
}
