using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal static class EquipmentForgingRules
{
    public static EquipmentForgingDecision Decide(EquipmentForgingTable table,
        EquipmentReinforcementEquipmentDefinition equipment, byte oldLevel,
        int materialItemId, int roll)
    {
        if (equipment.EquipmentTypeWeightIndex != 0)
            return new(EquipmentForgingRejection.NotWeapon);
        if (table.MaxLevel is <= 0 or > byte.MaxValue || table.Levels.Count < table.MaxLevel
            || equipment.Rarity < 0 || equipment.Rarity >= table.ItemWeightsByRarity.Count
            || !table.MaterialsByGrade.TryGetValue(equipment.Grade, out var material))
            return new(EquipmentForgingRejection.UnsupportedContent);
        if (oldLevel >= table.MaxLevel)
            return new(EquipmentForgingRejection.MaxLevel);
        var row = table.Levels[oldLevel];
        if (row.TargetLevel != oldLevel + 1 || row.SuccessWeight is < 0 or > 10000
            || !double.IsFinite(row.MaterialWeight) || row.MaterialWeight <= 0
            || material.ItemId <= 0 || material.BaseCount <= 0)
            return new(EquipmentForgingRejection.UnsupportedContent);
        if (materialItemId != material.ItemId)
            return new(EquipmentForgingRejection.MaterialUnavailable);
        if (roll is < 0 or >= 10000)
            return new(EquipmentForgingRejection.InvalidRequest);
        var rarityWeight = table.ItemWeightsByRarity[equipment.Rarity];
        var rawCost = material.BaseCount * rarityWeight * row.MaterialWeight;
        if (!double.IsFinite(rawCost) || rawCost <= 0 || rawCost > int.MaxValue - 1)
            return new(EquipmentForgingRejection.UnsupportedContent);
        var count = Math.Max(1, (int)Math.Floor(rawCost + 0.0000001));
        var succeeded = roll < row.SuccessWeight;
        return new(EquipmentForgingRejection.None, material.ItemId, count, succeeded,
            succeeded ? checked((byte)(oldLevel + 1)) : oldLevel, row.SuccessWeight, roll);
    }
}
