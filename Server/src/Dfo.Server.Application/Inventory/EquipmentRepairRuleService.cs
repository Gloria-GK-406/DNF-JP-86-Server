using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentRepairRuleService(IEquipmentRepairDefinitionSource definitions,
    IItemUseJudgementService uses) : IEquipmentRepairRuleService
{
    public async ValueTask<IEquipmentRepairCouponRule?> PrepareCouponAsync(int itemId, CancellationToken ct)
    {
        var rule = await uses.RuleForAsync(itemId, ct).ConfigureAwait(false);
        // The repair use case cannot silently discard additional potion effects.
        return rule is { IsReadable: true, IsConsumedOnUse: true, Action: "[repair coupon]",
            ExpBonusRate: 0, SummonsApc: false, SharedEffectSelector: null }
            ? new CouponRule(rule, uses.CooldownKeyFor(itemId, rule), uses) : null;
    }

    private sealed class CouponRule(ItemUseRule rule, ItemCooldownKey key, IItemUseJudgementService uses)
        : IEquipmentRepairCouponRule
    {
        public ItemCooldownKey CooldownKey => key;
        public TimeSpan Cooldown => TimeSpan.FromMilliseconds(rule.CooldownMilliseconds);
        public bool Allows(ItemUseCandidate candidate, int? dungeonId) =>
            uses.Judge(rule, candidate) == ItemUsability.Allowed
            && (rule.AllowedDungeonIds.Count == 0 || dungeonId is { } id && rule.AllowedDungeonIds.Contains(id));
    }

    public async ValueTask<IEquipmentRepairRule?> PrepareAsync(CancellationToken ct)
    {
        var rates = await definitions.RatesAsync(ct).ConfigureAwait(false);
        return rates is { } value ? new Rule(definitions, value) : null;
    }

    public async ValueTask<ushort?> AutoRepairDurabilityAsync(ItemSnapshot core, CancellationToken ct)
    {
        var definition = await definitions.GetAsync(core.ItemId, ct).ConfigureAwait(false);
        if (definition is null) return null;
        if (definition.MaxDurability == 0) return core.Durability;
        return core.Durability > definition.MaxDurability ? null : definition.MaxDurability;
    }

    private sealed class Rule(IEquipmentRepairDefinitionSource definitions,
        (float CostRate, IReadOnlyList<float> UpgradeRates) rates) : IEquipmentRepairRule
    {
        public async ValueTask<EquipmentRepairQuote?> QuoteAsync(ItemSnapshot core, CancellationToken ct)
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
