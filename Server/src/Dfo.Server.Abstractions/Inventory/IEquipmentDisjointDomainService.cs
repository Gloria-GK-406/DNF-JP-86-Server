using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Turns an equipment instance into rewards without changing inventory.</summary>
public interface IEquipmentDisjointRuleService
{
    ValueTask<EquipmentDisjointRuleResult> ResolveAsync(
        EquipmentDisjointCandidate equipment,
        EquipmentDisjointSettings settings,
        int randomSeed,
        CancellationToken cancellationToken);
}

/// <summary>Atomically replaces one piece of equipment with the materials decided by the rules.</summary>
public interface IEquipmentDisjointDomainService
{
    ValueTask<EquipmentDisjointResult> DisjointAsync(
        SelectedSessionIdentity session,
        short targetSlotIndex,
        InventoryContainerType itemSpace,
        short disjointItemSlotIndex,
        int contextValue,
        EquipmentDisjointSettings settings,
        CancellationToken cancellationToken);
}
