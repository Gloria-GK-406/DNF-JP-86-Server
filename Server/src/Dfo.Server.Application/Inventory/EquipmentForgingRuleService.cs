using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentForgingRuleService(
    IEquipmentReinforcementDefinitionSource equipment,
    IEquipmentForgingContentSource content) : IEquipmentForgingRuleService
{
    public async ValueTask<EquipmentForgingDecision> DecideAsync(int equipmentItemId,
        ushort durability, byte oldLevel, int materialItemId, int roll,
        CancellationToken ct)
    {
        var definition = await equipment.GetEquipmentAsync(equipmentItemId, ct)
            .ConfigureAwait(false);
        if (definition is null)
            return new(EquipmentForgingRejection.UnsupportedContent);
        if (definition.EquipmentTypeWeightIndex != 0)
            return new(EquipmentForgingRejection.NotWeapon);
        if (durability != definition.MaxDurability)
            return new(EquipmentForgingRejection.Durability);
        var table = await content.GetAsync(ct).ConfigureAwait(false);
        return table is null
            ? new(EquipmentForgingRejection.UnsupportedContent)
            : EquipmentForgingRules.Decide(table, definition, oldLevel, materialItemId, roll);
    }
}
