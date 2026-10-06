using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Inventory;

internal sealed partial class EquipmentUpgradeRuleService
{
    public async ValueTask<EquipmentReinforcementQuote> QuoteReinforcementAsync(ItemSnapshot core, CancellationToken ct,
        EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement)
    {
        if (kind is not (EquipmentUpgradeKind.Reinforcement or EquipmentUpgradeKind.Amplification))
            return new(EquipmentReinforcementRejection.InvalidRequest);
        if (kind == EquipmentUpgradeKind.Amplification)
        {
            var eligibility = await CheckAmplificationAsync(core, ct).ConfigureAwait(false);
            if (eligibility.Rejection != EquipmentAmplificationRejection.None)
                return new(eligibility.Rejection switch
                {
                    EquipmentAmplificationRejection.Durability => EquipmentReinforcementRejection.Durability,
                    EquipmentAmplificationRejection.UnsupportedContent => EquipmentReinforcementRejection.UnsupportedContent,
                    _ => EquipmentReinforcementRejection.CannotReinforce,
                });
        }
        else if (core.SealFlag != 0 || core.AmplifyType != 0)
            return new(EquipmentReinforcementRejection.CannotReinforce);
        var equipment = await equipmentDefinitions.GetEquipmentAsync(core.ItemId, ct).ConfigureAwait(false);
        var table = kind == EquipmentUpgradeKind.Amplification
            ? await definitions.GetAmplificationTableAsync(ct).ConfigureAwait(false)
            : await definitions.GetNormalTableAsync(ct).ConfigureAwait(false);
        if (equipment is null || table is null || WeightIndex(equipment.EquipmentType) is null or 1
            || equipment.ImpossibleContents.Contains("upgrade"))
            return new(EquipmentReinforcementRejection.UnsupportedContent);
        if (equipment.MaxDurability is { } maximumDurability && core.Durability != maximumDurability)
            return new(EquipmentReinforcementRejection.Durability);
        var oldLevel = core.ReinforcementLevel;
        // A21 admission remains capped at current level 30, regardless of additional archive rows.
        if (oldLevel > 30 || table.MaxUpgradeLevelByRarity.TryGetValue(equipment.Rarity, out var maximum)
            && oldLevel >= maximum)
            return new(EquipmentReinforcementRejection.CannotReinforce);
        var row = table.Rows.FirstOrDefault(candidate => candidate.TargetLevel == oldLevel + 1);
        if (row is null || row.FailureThreshold is < 0 or > 100_000
            || row.MaterialItemId <= 0 || row.MaterialCount <= 0
            || row.PenaltyType is < 1 or > 3 || row.PenaltyValue < 0
            || !TryGoldCost(table, equipment, oldLevel, out var goldCost))
            return new(EquipmentReinforcementRejection.UnsupportedContent);
        var canDestroy = EquipmentReinforcementFailureRuleService.PenaltyType(
            oldLevel, equipment.Rarity, table, row) == 3;
        return new(EquipmentReinforcementRejection.None,
            new Rule(definitions, logger, core.ItemId, row, oldLevel, canDestroy, goldCost, kind));
    }

    private sealed class Rule(IEquipmentReinforcementDefinitionSource definitions,
        ILogger<EquipmentUpgradeRuleService> logger, int itemId,
        EquipmentReinforcementRow row, byte oldLevel, bool canDestroy, int goldCost,
        EquipmentUpgradeKind kind) : IEquipmentReinforcementRule
    {
        public int GoldCost => goldCost;
        public int MaterialItemId => row.MaterialItemId;
        public int MaterialCount => row.MaterialCount;
        public bool CanDestroy => canDestroy;
        public bool CanUseAutomaticProtection => canDestroy
            || kind == EquipmentUpgradeKind.Amplification && row.PenaltyType == 2;
        public ValueTask<EquipmentReinforcementProtectionDefinition?> ProtectionAsync(int itemId, CancellationToken ct) =>
            definitions.GetProtectionAsync(itemId, ct, kind);

        public async ValueTask<EquipmentProtectionChoice?> SelectAutomaticProtectionAsync(
            IReadOnlyList<EquipmentProtectionCandidate> candidates, CancellationToken ct)
        {
            var choices = new List<EquipmentProtectionChoice>();
            foreach (var candidate in candidates)
            {
                var definition = await ProtectionAsync(candidate.ItemId, ct).ConfigureAwait(false);
                if (definition is null || !canDestroy && definition.Kind == EquipmentReinforcementProtectionKind.ResetToZero
                    || definition.Kind == EquipmentReinforcementProtectionKind.RetainLevel
                    && oldLevel <= definition.RetainedLevel) continue;
                choices.Add(new(candidate.Slot, definition));
            }
            // The super-ticket script gives it priority; inventory order breaks same-kind ties.
            return choices.OrderByDescending(choice =>
                    choice.Definition.Kind == EquipmentReinforcementProtectionKind.RetainLevel)
                .ThenBy(choice => choice.Slot.Space).ThenBy(choice => choice.Slot.Index).FirstOrDefault();
        }

        public bool TryReinforce(int draw)
        {
            var succeeded = EquipmentReinforcementRules.Succeeds(row.FailureThreshold, draw);
            logger.LogInformation(
                "REINFORCEMENT_ROLL kind={Kind} item={ItemId} old={OldLevel} target={TargetLevel} draw={Draw} failureThreshold={FailureThreshold} scale=100000 succeeded={Succeeded}",
                kind, itemId, oldLevel, row.TargetLevel, draw, row.FailureThreshold, succeeded);
            return succeeded;
        }
    }

    private static bool TryGoldCost(EquipmentReinforcementTable table,
        EquipmentDefinition equipment, byte oldLevel, out int cost)
    {
        cost = 0;
        var typeIndex = WeightIndex(equipment.EquipmentType);
        var levelIndex = equipment.MinimumLevel + Math.Max(0, equipment.Rarity - 1) * 2;
        if (levelIndex < 0 || levelIndex >= table.BaseGoldByEquipmentLevel.Count
            || equipment.Rarity < 0 || equipment.Rarity >= table.CostWeightsByRarity.Count
            || typeIndex is null
            || typeIndex.Value >= table.CostWeightsByEquipmentType.Count)
            return false;
        // A21's normal table continues beyond the last explicit cost-weight entry.
        // The upstream provider treats a missing level weight as the neutral factor.
        var levelWeight = table.CostWeightByCurrentLevel.GetValueOrDefault(oldLevel, 1);
        if (!double.IsFinite(levelWeight) || levelWeight <= 0)
            return false;
        var raw = (double)table.BaseGoldByEquipmentLevel[levelIndex]
            * table.CostWeightsByRarity[equipment.Rarity]
            * table.CostWeightsByEquipmentType[typeIndex.Value]
            * levelWeight;
        if (!double.IsFinite(raw) || raw < 0 || raw > int.MaxValue)
            return false;
        cost = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        return true;
    }
}
