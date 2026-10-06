using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Inventory;

internal sealed partial class EquipmentUpgradeRuleService(
    IEquipmentDefinitionSource equipmentDefinitions,
    IEquipmentReinforcementDefinitionSource definitions,
    IEquipmentOutworldContentSource outworldContent,
    IEquipmentForgingContentSource forgingContent,
    ILogger<EquipmentUpgradeRuleService> logger) : IEquipmentUpgradeRuleService
{
    public async ValueTask<EquipmentAmplificationEligibility> CheckAmplificationAsync(
        ItemSnapshot core, CancellationToken ct)
    {
        var equipment = await equipmentDefinitions.GetEquipmentAsync(core.ItemId, ct).ConfigureAwait(false);
        if (equipment is null || WeightIndex(equipment.EquipmentType) is null or 1 || equipment.Rarity < 2)
            return new(EquipmentAmplificationRejection.UnsupportedContent);
        var content = await outworldContent.GetAsync(ct).ConfigureAwait(false);
        if (content is null || equipment.MinimumLevel < content.MinimumEquipmentLevel)
            return new(EquipmentAmplificationRejection.UnsupportedContent);
        if (equipment.ImpossibleContents.Contains("upgrade") || equipment.ImpossibleContents.Contains("amplify upgrade"))
            return new(EquipmentAmplificationRejection.Forbidden);
        if (core.SealFlag != 0) return new(EquipmentAmplificationRejection.Sealed);
        if (core.AmplifyType == 0x80 || core.AmplifyType is >= 1 and <= 4 && core.AmplifyValue == 0)
            return new(EquipmentAmplificationRejection.UnidentifiedOption);
        if (core.AmplifyType is not (>= 1 and <= 4)) return new(EquipmentAmplificationRejection.NoOption);
        if (equipment.MaxDurability is { } maximum && core.Durability != maximum)
            return new(EquipmentAmplificationRejection.Durability);
        return new(EquipmentAmplificationRejection.None);
    }

    // This ordering belongs to the ordinary reinforcement cost table, not to equipment metadata.
    private static int? WeightIndex(string equipmentType) => equipmentType switch
    {
        "weapon" => 0,
        "title name" => 1,
        "coat" => 2,
        "shoulder" => 3,
        "pants" => 4,
        "shoes" => 5,
        "waist" => 6,
        "amulet" => 7,
        "wrist" => 8,
        "ring" => 9,
        "support" => 10,
        "magic stone" => 11,
        _ => null,
    };
}
