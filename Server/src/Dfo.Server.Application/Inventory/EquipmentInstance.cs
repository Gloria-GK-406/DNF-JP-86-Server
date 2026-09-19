using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// One loaded piece of equipment during a use case. Its durable state remains in the item container;
/// this instance only applies equipment-local transitions before the caller commits the new core.
/// </summary>
internal sealed class EquipmentInstance
{
    private EquipmentInstance(ContainerSlotItem item)
    {
        SlotIndex = item.SlotIndex;
        Core = item.Core;
        OriginalDurability = item.Core.Durability;
    }

    internal int SlotIndex { get; }

    internal InventoryItemCoreState Core { get; private set; }

    internal ushort OriginalDurability { get; }

    internal static EquipmentInstance? From(ContainerSlotItem? item) =>
        item is { WorldClass: ItemWorldClass.Equipment } ? new(item) : null;

    /// <summary>
    /// Gives a disjoint rule the equipment facts it may judge, but neither its container nor a
    /// mutation surface. Settlement remains the calling domain service's responsibility.
    /// </summary>
    internal EquipmentDisjointCandidate DisjointCandidate() => new(Core.ItemId, Core);

    /// <summary>Wear the piece down, never below zero. A broken or non-durable piece is unchanged.</summary>
    internal bool DecreaseDurability(ushort amount)
    {
        if (amount == 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        var remaining = (ushort)Math.Max(0, Core.Durability - amount);
        if (remaining == Core.Durability)
            return false;
        Core = Core with { Durability = remaining };
        return true;
    }

    internal EquipmentRepairDecision? Repair(EquipmentRepairDefinition definition,
        (float CostRate, IReadOnlyList<float> UpgradeRates) rates)
    {
        // A valid equipment declaration without durability is a successful no-op, not a failed
        // quote. The item retains exactly its stored state and contributes zero to the charge.
        if (definition is { RepairPrice: 0, MaxDurability: 0 })
            return new(0);

        if (Core.Durability > definition.MaxDurability
            || Core.EnchantUpgradeCount >= rates.UpgradeRates.Count)
            return null;
        var lost = definition.MaxDurability - Core.Durability;
        // The observed A21 quote truncates after float32 arithmetic; grade adjustment is integer division.
        var basePrice = (long)definition.RepairPrice * (definition.Grade + 5L) / 10;
        var raw = (float)basePrice * rates.CostRate / definition.MaxDurability * lost
            * rates.UpgradeRates[Core.EnchantUpgradeCount];
        if (!float.IsFinite(raw) || raw < 0 || raw > int.MaxValue)
            return null;
        Core = Core with { Durability = definition.MaxDurability };
        return new((int)raw);
    }
}

internal readonly record struct EquipmentRepairDecision(int Cost);
