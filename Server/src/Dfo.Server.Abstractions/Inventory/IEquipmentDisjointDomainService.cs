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

/// <summary>
/// Atomically replaces one piece of equipment with rule-selected materials, with a portable tool
/// consumed in that same settlement when that explicit entry point is used.
/// </summary>
public interface IEquipmentDisjointDomainService
{
    ValueTask<EquipmentDisjointResult> DisjointAtNpcAsync(
        SelectedSessionIdentity session,
        BusinessSlot targetSlot,
        CancellationToken cancellationToken);

    ValueTask<EquipmentDisjointResult> DisjointWithPortableMachineAsync(
        SelectedSessionIdentity session,
        BusinessSlot targetSlot,
        BusinessSlot disjointToolSlot,
        CancellationToken cancellationToken);
}
