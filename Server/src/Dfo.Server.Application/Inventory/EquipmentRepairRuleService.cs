using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentRepairRuleService(IEquipmentRepairDefinitionSource definitions) : IEquipmentRepairRuleService
{
    public async ValueTask<IEquipmentRepairRule?> PrepareAsync(CancellationToken ct)
    {
        var rates = await definitions.RatesAsync(ct).ConfigureAwait(false);
        return rates is { } value ? new Rule(definitions, value) : null;
    }

    public async ValueTask<ushort?> AutoRepairDurabilityAsync(InventoryItemCoreState core, CancellationToken ct)
    {
        var definition = await definitions.GetAsync(core.ItemId, ct).ConfigureAwait(false);
        if (definition is null) return null;
        if (definition.MaxDurability == 0) return core.Durability;
        return core.Durability > definition.MaxDurability ? null : definition.MaxDurability;
    }

    private sealed class Rule(IEquipmentRepairDefinitionSource definitions,
        (float CostRate, IReadOnlyList<float> UpgradeRates) rates) : IEquipmentRepairRule
    {
        public async ValueTask<EquipmentRepairQuote?> QuoteAsync(InventoryItemCoreState core, CancellationToken ct)
        {
            var definition = await definitions.GetAsync(core.ItemId, ct).ConfigureAwait(false);
            if (definition is null) return null;
            if (definition is { RepairPrice: 0, MaxDurability: 0 }) return new(0, core.Durability, false);
            if (core.Durability > definition.MaxDurability || core.ReinforcementLevel >= rates.UpgradeRates.Count)
                return null;
            var lost = definition.MaxDurability - core.Durability;
            // A21 truncates after float32 arithmetic; grade adjustment uses integer division.
            var basePrice = (long)definition.RepairPrice * (definition.Grade + 5L) / 10;
            var raw = (float)basePrice * rates.CostRate / definition.MaxDurability * lost
                * rates.UpgradeRates[core.ReinforcementLevel];
            if (!float.IsFinite(raw) || raw < 0 || raw > int.MaxValue) return null;
            return new((int)raw, definition.MaxDurability, core.Durability != definition.MaxDurability);
        }
    }
}
