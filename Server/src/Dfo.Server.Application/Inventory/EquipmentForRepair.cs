using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>A loaded equipment instance deciding its own repair cost and resulting durability.</summary>
internal sealed record EquipmentForRepair(ContainerSlotItem Item, EquipmentRepairDefinition Definition)
{
    internal EquipmentRepairDecision? Repair((float CostRate, IReadOnlyList<float> UpgradeRates) rates)
    {
        // A valid equipment declaration without durability is a successful no-op, not a failed
        // quote. The item retains exactly its stored state and contributes zero to the charge.
        if (Definition is { RepairPrice: 0, MaxDurability: 0 })
            return new(0, Item.Core.Durability);

        if (Item.Core.Durability > Definition.MaxDurability
            || Item.Core.EnchantUpgradeCount >= rates.UpgradeRates.Count)
            return null;
        var lost = Definition.MaxDurability - Item.Core.Durability;
        // The observed A21 quote truncates after float32 arithmetic; grade adjustment is integer division.
        var basePrice = (long)Definition.RepairPrice * (Definition.Grade + 5L) / 10;
        var raw = (float)basePrice * rates.CostRate / Definition.MaxDurability * lost
            * rates.UpgradeRates[Item.Core.EnchantUpgradeCount];
        if (!float.IsFinite(raw) || raw < 0 || raw > int.MaxValue)
            return null;
        return new((int)raw, Definition.MaxDurability);
    }
}

internal readonly record struct EquipmentRepairDecision(int Cost, ushort DurabilityAfter);
