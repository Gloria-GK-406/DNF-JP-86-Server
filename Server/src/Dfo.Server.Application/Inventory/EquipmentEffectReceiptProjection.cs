using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal static class EquipmentEffectReceiptProjection
{
    public static async ValueTask<IReadOnlyList<InventorySlotEffectChange>> FinalSlotsAsync(CharacterId owner,
        ApplyEffectsResult applied, IBusinessInventorySlotDomainService slots, CancellationToken ct)
    {
        var touched = applied.Changes<ContainerConsumptionChange>().Select(change => change.Slot)
            .Concat(applied.Changes<EquipmentEffectChange>().Select(change => change.Slot))
            .Concat(applied.Changes<StackableGrantEffectChange>().SelectMany(change => change.Slots.Select(slot => slot.Slot)))
            .Distinct().OrderBy(slot => slot.Space).ThenBy(slot => slot.Index);
        var result = new List<InventorySlotEffectChange>();
        foreach (var slot in touched)
            result.Add(new(slot, (await slots.Get(owner, slot).Container.GetAsync(slot.Index, ct).ConfigureAwait(false))?.Snapshot));
        return result.AsReadOnly();
    }

    public static IReadOnlyList<StackableGrantAddition> Rewards(ApplyEffectsResult applied) =>
        applied.Changes<StackableGrantEffectChange>().SelectMany(change => change.Additions).ToArray();
}
