using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentOutworldRuleService(
    IEquipmentOutworldContentSource content,
    IEquipmentReinforcementDefinitionSource equipment)
    : IEquipmentOutworldRuleService
{
    public async ValueTask<EquipmentOutworldDecision> DecideAsync(
        EquipmentOutworldAction action, int equipmentItemId, byte amplifyType,
        byte upgradeLevel, int materialItemId, byte selectedOption, int seed,
        CancellationToken ct)
    {
        if (equipmentItemId <= 0 || materialItemId <= 0 || action is not
            (EquipmentOutworldAction.Purify or EquipmentOutworldAction.Invest
                or EquipmentOutworldAction.Twist))
            return new(EquipmentOutworldRejection.InvalidRequest);
        var definition = await equipment.GetEquipmentAsync(equipmentItemId, ct).ConfigureAwait(false);
        var table = await content.GetAsync(ct).ConfigureAwait(false);
        if (definition is null || table is null || definition.MinimumLevel < table.MinimumEquipmentLevel
            || definition.Rarity < 2 || definition.EquipmentTypeWeightIndex == 1
            || !table.InitialValues.TryGetValue(definition.Rarity, out var values))
            return new(EquipmentOutworldRejection.UnsupportedContent);
        if (upgradeLevel != 0)
            return new(EquipmentOutworldRejection.AlreadyUpgraded);

        if (action == EquipmentOutworldAction.Purify)
        {
            if (amplifyType != 0x80) return new(EquipmentOutworldRejection.InvalidState);
            if (table.ClearMaterials.TryGetValue(materialItemId, out var clearCount))
                return new(EquipmentOutworldRejection.None, clearCount, 0, 0, true, seed);
            if (!table.PurifyMaterials.TryGetValue(materialItemId, out var purifyCount))
                return new(EquipmentOutworldRejection.MaterialUnavailable);
            var attribute = Roll(table.RollWeights, seed);
            if (attribute is < 1 or > 4 || !values.TryGetValue(attribute, out var rolledValue))
                return new(EquipmentOutworldRejection.UnsupportedContent);
            return new(EquipmentOutworldRejection.None, purifyCount, attribute, rolledValue,
                false, seed);
        }

        if (action == EquipmentOutworldAction.Invest && amplifyType != 0)
            return new(EquipmentOutworldRejection.AlreadyHasAmplifyOption);
        if (action == EquipmentOutworldAction.Twist && amplifyType is not (>= 1 and <= 4))
            return new(EquipmentOutworldRejection.NoAmplifyOption);
        var materials = action == EquipmentOutworldAction.Invest
            ? table.InvestMaterials : table.TwistMaterials;
        if (!materials.TryGetValue(materialItemId, out var material))
            return new(EquipmentOutworldRejection.MaterialUnavailable);
        var targetType = material.Attribute == 0 ? selectedOption : material.Attribute;
        if (targetType is < 1 or > 4 || !values.TryGetValue(targetType, out var value))
            return new(EquipmentOutworldRejection.InvalidRequest);
        if (action == EquipmentOutworldAction.Twist && targetType == amplifyType)
            return new(EquipmentOutworldRejection.SameOption);
        return new(EquipmentOutworldRejection.None, material.Count, targetType, value,
            false, seed);
    }

    private static byte Roll(IReadOnlyList<EquipmentOutworldRollWeight> weights, int seed)
    {
        if (weights.Count != 4 || weights.Any(entry => entry.Weight <= 0)
            || weights.Select(entry => entry.Attribute).Distinct().Count() != 4)
            return 0;
        var total = weights.Sum(entry => (long)entry.Weight);
        if (total is <= 0 or > int.MaxValue) return 0;
        var draw = new Random(seed).Next((int)total);
        foreach (var entry in weights)
        {
            draw -= entry.Weight;
            if (draw < 0) return entry.Attribute;
        }
        return 0;
    }
}
