using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed partial class EquipmentUpgradeRuleService
{
    public async ValueTask<EquipmentForgingDecision> DecideForgingAsync(int equipmentItemId,
        ushort durability, byte oldLevel, int materialItemId, int roll,
        CancellationToken ct)
    {
        var definition = await equipmentDefinitions.GetEquipmentAsync(equipmentItemId, ct)
            .ConfigureAwait(false);
        if (definition is null)
            return new(EquipmentForgingRejection.UnsupportedContent);
        if (definition.EquipmentType != "weapon")
            return new(EquipmentForgingRejection.NotWeapon);
        if (definition.MaxDurability is { } maximum && durability != maximum)
            return new(EquipmentForgingRejection.Durability);
        var table = await forgingContent.GetAsync(ct).ConfigureAwait(false);
        return table is null
            ? new(EquipmentForgingRejection.UnsupportedContent)
            : EquipmentForgingRules.Decide(table, definition, oldLevel, materialItemId, roll);
    }
}
