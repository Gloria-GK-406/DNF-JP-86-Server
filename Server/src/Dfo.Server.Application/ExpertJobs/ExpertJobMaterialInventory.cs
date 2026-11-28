using System.Collections.Immutable;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.ExpertJobs;

internal static class ExpertJobMaterialInventory
{
    private static readonly ImmutableArray<BusinessInventorySpace> MaterialSpaces = [BusinessInventorySpace.QuickSlot,
        BusinessInventorySpace.Consumable, BusinessInventorySpace.Material, BusinessInventorySpace.Quest,
        BusinessInventorySpace.ExpertMaterial, BusinessInventorySpace.SharedMaterial];

    public static async ValueTask<IReadOnlyList<OpeningHeldItem>> ReadAsync(ICharacterBusinessInventoryDomainService bags,
        CharacterId owner, CancellationToken ct, BusinessSlot? excluded = null)
    {
        var held = new List<OpeningHeldItem>();
        foreach (var space in MaterialSpaces)
        {
            var container = bags.GetStore(owner, BusinessInventoryOwnership.StoreOf(space)).FindContainer(space);
            if (container is null) continue;
            foreach (var item in await container.GetAllAsync(ct).ConfigureAwait(false))
            {
                var slot = new BusinessSlot(space, item.SlotIndex);
                if (slot != excluded) held.Add(new(slot, item));
            }
        }
        return held;
    }
}
