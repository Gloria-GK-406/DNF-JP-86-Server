using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Inventory;

internal sealed partial class EquipmentUpgradeRuleService
{
    private static bool AllowsMedalUpgrade(EquipmentUpgradeKind kind, bool hasProtection) =>
        kind == EquipmentUpgradeKind.Reinforcement && !hasProtection;

    public static bool AcceptsReinforcementSlot(BusinessInventorySpace space, EquipmentUpgradeKind kind, bool hasProtection) =>
        space == BusinessInventorySpace.GuildMedal ? AllowsMedalUpgrade(kind, hasProtection)
            : space.IsCarriedItemSpace() || space.IsWornEquipmentSpace();

    public static bool AcceptsTarget(BusinessInventorySpace space, ItemSnapshot target, DateTimeOffset now)
    {
        var medal = target.Type.HasFlag(ItemTraits.GuildMedal);
        return medal == (space == BusinessInventorySpace.GuildMedal)
            && (!medal || !target.IsMirror && !target.Lifetime.IsExpired(now));
    }

    public static bool AcceptsMaterial(ItemSnapshot target, ItemSnapshot material, DateTimeOffset now) =>
        !target.Type.HasFlag(ItemTraits.GuildMedal) || material.EquipmentLockId == 0
            && material.SortLockFlag == 0 && !material.Lifetime.IsExpired(now);

    public async ValueTask<EquipmentReinforcementQuote> QuoteReinforcementAsync(ItemSnapshot core, CancellationToken ct,
        EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement, int? materialItemId = null, bool hasProtection = false)
    {
        var medal = core.Type.HasFlag(ItemTraits.GuildMedal);
        if (!medal && materialItemId is { } material)
        {
            var ticket = await QuoteTicketAsync(core, material, ct, kind).ConfigureAwait(false);
            if (ticket.IsTicket)
                return hasProtection && ticket.Rejection == EquipmentReinforcementRejection.None
                    ? new(EquipmentReinforcementRejection.InvalidRequest, IsTicket: true) : ticket;
        }
        if (kind is not (EquipmentUpgradeKind.Reinforcement or EquipmentUpgradeKind.Amplification)
            || medal && !AllowsMedalUpgrade(kind, hasProtection))
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
        var table = medal ? await definitions.GetGuildMedalTableAsync(ct).ConfigureAwait(false)
            : kind == EquipmentUpgradeKind.Amplification
            ? await definitions.GetAmplificationTableAsync(ct).ConfigureAwait(false)
            : await definitions.GetNormalTableAsync(ct).ConfigureAwait(false);
        if (equipment is null || table is null
            || (medal ? equipment.EquipmentType != "flag" : WeightIndex(equipment.EquipmentType) is null or 1)
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
            || !TryGoldCost(table, equipment, oldLevel, medal, out var goldCost))
            return new(EquipmentReinforcementRejection.UnsupportedContent);
        if (medal)
        {
            if (equipment.Rarity >= table.MedalMaterialWeights.Count)
                return new(EquipmentReinforcementRejection.UnsupportedContent);
            var quantity = row.MaterialCount * (double)(float)table.MedalMaterialWeights[equipment.Rarity];
            if (!double.IsFinite(quantity) || quantity is < 1 or > int.MaxValue)
                return new(EquipmentReinforcementRejection.UnsupportedContent);
            row = row with { MaterialCount = (int)quantity };
        }
        var canDestroy = (medal ? row.PenaltyType : EquipmentReinforcementFailureRuleService.PenaltyType(
            oldLevel, equipment.Rarity, table, row)) == 3;
        return new(EquipmentReinforcementRejection.None,
            new Rule(definitions, logger, core.ItemId, row, oldLevel, canDestroy, goldCost, kind, medal));
    }

    public async ValueTask<int?> NoticeLevelAsync(EquipmentUpgradeKind kind, CancellationToken ct)
    {
        var table = kind == EquipmentUpgradeKind.Amplification
            ? await definitions.GetAmplificationTableAsync(ct).ConfigureAwait(false)
            : await definitions.GetNormalTableAsync(ct).ConfigureAwait(false);
        return table?.NoticeLevel;
    }

    private sealed class Rule(IEquipmentReinforcementDefinitionSource definitions,
        ILogger<EquipmentUpgradeRuleService> logger, int itemId,
        EquipmentReinforcementRow row, byte oldLevel, bool canDestroy, int goldCost,
        EquipmentUpgradeKind kind, bool medal) : IEquipmentReinforcementRule
    {
        public int GoldCost => goldCost;
        public int MaterialItemId => row.MaterialItemId;
        public int MaterialCount => row.MaterialCount;
        public bool CanDestroy => canDestroy;
        public bool CanUseAutomaticProtection => !medal && (canDestroy
            || kind == EquipmentUpgradeKind.Amplification && row.PenaltyType == 2);
        public ValueTask<EquipmentReinforcementProtectionDefinition?> ProtectionAsync(int itemId, CancellationToken ct) =>
            medal ? ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(null) : definitions.GetProtectionAsync(itemId, ct, kind);

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

        public bool TryReinforce(int draw, int successBonusUnits = 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(successBonusUnits);
            var failureThreshold = !medal && kind == EquipmentUpgradeKind.Reinforcement
                ? Math.Max(0, row.FailureThreshold - successBonusUnits) : row.FailureThreshold;
            var succeeded = EquipmentReinforcementRules.Succeeds(failureThreshold, draw);
            logger.LogInformation(
                "REINFORCEMENT_ROLL kind={Kind} item={ItemId} old={OldLevel} target={TargetLevel} draw={Draw} failureThreshold={FailureThreshold} scale=100000 succeeded={Succeeded}",
                kind, itemId, oldLevel, row.TargetLevel, draw, failureThreshold, succeeded);
            return succeeded;
        }
    }

    private static bool TryGoldCost(EquipmentReinforcementTable table,
        EquipmentDefinition equipment, byte oldLevel, bool medal, out int cost)
    {
        cost = 0;
        var typeIndex = medal ? 19 : WeightIndex(equipment.EquipmentType);
        var levelIndex = equipment.MinimumLevel + Math.Max(0, equipment.Rarity - 1) * 2;
        if (levelIndex < 0 || levelIndex >= table.BaseGoldByEquipmentLevel.Count
            || equipment.Rarity < 0 || equipment.Rarity >= table.CostWeightsByRarity.Count
            || typeIndex is null
            || !medal && typeIndex.Value >= table.CostWeightsByEquipmentType.Count)
            return false;
        // A21's normal table continues beyond the last explicit cost-weight entry.
        // The upstream provider treats a missing level weight as the neutral factor.
        var levelWeight = table.CostWeightByCurrentLevel.GetValueOrDefault(oldLevel, 1);
        if (!double.IsFinite(levelWeight) || levelWeight <= 0)
            return false;
        var raw = (double)table.BaseGoldByEquipmentLevel[levelIndex]
            * table.CostWeightsByRarity[equipment.Rarity]
            * (typeIndex.Value < table.CostWeightsByEquipmentType.Count ? table.CostWeightsByEquipmentType[typeIndex.Value] : 1)
            * levelWeight;
        if (!double.IsFinite(raw) || raw < 0 || raw > int.MaxValue)
            return false;
        cost = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        return true;
    }
}
