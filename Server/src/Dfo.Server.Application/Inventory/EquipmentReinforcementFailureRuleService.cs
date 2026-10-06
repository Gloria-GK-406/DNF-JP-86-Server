using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentReinforcementFailureRuleService(
    IEquipmentReinforcementDefinitionSource reinforcement,
    IEquipmentDefinitionSource equipmentDefinitions,
    IEquipmentDisjointDefinitionSource disjoint,
    ILogger<EquipmentReinforcementFailureRuleService> logger) : IEquipmentReinforcementFailureRuleService
{
    public async ValueTask<EquipmentReinforcementFailureResult> GetResultAsync(ItemReference equipment,
        EquipmentReinforcementFailureContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(equipment);
        ArgumentNullException.ThrowIfNull(context);
        ValidateDraw(context.AdditionalItemDraw);
        ValidateDraw(context.GreatDraw);
        var definition = await equipmentDefinitions.GetEquipmentAsync(equipment.Snapshot.ItemId, ct).ConfigureAwait(false);
        var table = context.Kind switch
        {
            EquipmentUpgradeKind.Reinforcement => await reinforcement.GetNormalTableAsync(ct).ConfigureAwait(false),
            EquipmentUpgradeKind.Amplification => await reinforcement.GetAmplificationTableAsync(ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(context)),
        };
        var oldLevel = equipment.Snapshot.ReinforcementLevel;
        var row = table?.Rows.FirstOrDefault(candidate => candidate.TargetLevel == oldLevel + 1);
        if (definition is null || table is null || row is null
            || row.PenaltyType is < 1 or > 3 || row.PenaltyValue < 0)
            return Unsupported();
        var penalty = PenaltyType(oldLevel, definition.Rarity, table, row);
        var result = context.Kind == EquipmentUpgradeKind.Amplification && penalty == 2
            && context.Protection is { Kind: EquipmentReinforcementProtectionKind.RetainLevel, RetainedLevel: >= 0 and <= 31 } ticket
            && oldLevel > ticket.RetainedLevel
            ? new EquipmentReinforcementFailureResult(EquipmentReinforcementRejection.None,
                EquipmentReinforcementOutcome.Downgraded, (byte)ticket.RetainedLevel, true, [])
            : ResolvePenalty(oldLevel, penalty, row.PenaltyValue, context.Protection);
        if (result.Outcome == EquipmentReinforcementOutcome.Destroyed)
        {
            var source = await disjoint.GetDestructionEquipmentAsync(equipment.Snapshot.ItemId, ct).ConfigureAwait(false);
            var disjointTable = await disjoint.GetSystemTableAsync(ct).ConfigureAwait(false);
            var rewards = new List<EquipmentReinforcementFailureReward>();
            if (source is null || disjointTable is null || source.Rarity != definition.Rarity
                || source.Grade != definition.Grade
                || !TryBaseRewards(source, disjointTable, context, rewards)
                || !TryAdditionalBonus(table.DestroyBonus, definition, oldLevel, rewards))
                return Unsupported();
            result = result with { Rewards = Array.AsReadOnly(rewards.ToArray()) };
        }
        logger.LogInformation(
            "REINFORCEMENT_FAILURE kind={Kind} item={ItemId} old={OldLevel} penalty={PenaltyType} calculatedOutcome={Outcome} calculatedLevel={NewLevel} consumeProtection={ConsumeProtection} rewardKinds={RewardKinds}",
            context.Kind, equipment.Snapshot.ItemId, oldLevel, penalty, result.Outcome, result.NewLevel,
            result.ConsumeProtection, result.Rewards.Count);
        return result;
    }

    // Quote exposes only this eligibility hint to select protection before drawing.
    // Failure policy has one owner even when an archive rarity overrides the table row.
    internal static int PenaltyType(byte oldLevel, int rarity, EquipmentReinforcementTable table,
        EquipmentReinforcementRow row) =>
        table.DestroyLevelByRarity.TryGetValue(rarity, out var threshold) && oldLevel >= threshold
            ? 3 : row.PenaltyType;

    internal static EquipmentReinforcementFailureResult ResolvePenalty(byte oldLevel, int penalty, int value,
        EquipmentReinforcementProtectionDefinition? protection)
    {
        if (penalty is < 1 or > 3 || value < 0) throw new ArgumentOutOfRangeException(nameof(penalty));
        if (penalty == 1) return new(EquipmentReinforcementRejection.None,
            EquipmentReinforcementOutcome.Retained, oldLevel, false, []);
        if (penalty == 2) return new(EquipmentReinforcementRejection.None,
            EquipmentReinforcementOutcome.Downgraded, (byte)Math.Max(0, oldLevel - value), false, []);
        if (protection is { RetainedLevel: >= 0 and <= 31 } ticket
            && (ticket.Kind == EquipmentReinforcementProtectionKind.ResetToZero
                || ticket.Kind == EquipmentReinforcementProtectionKind.RetainLevel && oldLevel > ticket.RetainedLevel))
            return new(EquipmentReinforcementRejection.None, EquipmentReinforcementOutcome.Downgraded,
                (byte)Math.Min(oldLevel, ticket.RetainedLevel), true, []);
        return new(EquipmentReinforcementRejection.None, EquipmentReinforcementOutcome.Destroyed, 0, false, []);
    }

    private static EquipmentReinforcementFailureResult Unsupported() =>
        new(EquipmentReinforcementRejection.UnsupportedOutcome, EquipmentReinforcementOutcome.Destroyed, 0, false, []);

    private static void ValidateDraw(int draw)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(draw);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(draw, 10_000);
    }

    internal static bool TryBaseRewards(EquipmentDestructionDefinition equipment, EquipmentDisjointTable table,
        EquipmentReinforcementFailureContext context, List<EquipmentReinforcementFailureReward> rewards)
    {
        var rarity = equipment.Rarity;
        if (equipment.Value < 0 || equipment.Grade < 0 || equipment.SaleRate < 0 || rarity < 0
            || rarity >= table.CubeCreationMultipliers.Count || rarity >= table.AdditionalResults.Count
            || table.CubeItemId <= 0 || !double.IsFinite(table.CubeCreationBase)
            || table.CubeCreationBase is <= 0 or > int.MaxValue || table.CubeCreationBase != Math.Truncate(table.CubeCreationBase))
            return false;
        // 70 GetResult: float32 1.1 coefficient, truncation, sale-rate division,
        // integer cube-base division, then float32 rarity coefficient / 1000 and truncation.
        var adjusted = equipment.Value * (double)1.1f;
        if (adjusted > int.MaxValue) return false;
        var sale = (long)(int)adjusted * equipment.SaleRate / 1000;
        if (sale > int.MaxValue / 1000) return false;
        var units = sale * 1000 / (int)table.CubeCreationBase;
        var coefficient = (float)table.CubeCreationMultipliers[rarity];
        var cubes = units * (double)coefficient / 1000;
        if (!double.IsFinite(cubes) || cubes < 0 || cubes > int.MaxValue) return false;
        rewards.Add(new(table.CubeItemId, Math.Max(1, (int)cubes)));
        var candidates = table.AdditionalResults[rarity];
        if (candidates.Count == 0) return true;
        if (rarity >= table.AdditionalRules.Count || candidates.Any(itemId => itemId <= 0)) return false;
        var rule = table.AdditionalRules[rarity];
        if (!double.IsFinite(rule.GreatChancePercent) || rule.GreatChancePercent is < 0 or > 100
            || rule.GreatChancePercent != Math.Truncate(rule.GreatChancePercent))
            return false;
        var divisor = (float)(context.GreatDraw < (int)rule.GreatChancePercent * 100
            ? rule.GreatCountDivisor : rule.NormalCountDivisor);
        if (!float.IsFinite(divisor) || divisor <= 0) return false;
        // Both 70 branches store the quotient in a float32 temporary before truncating.
        var quantity = equipment.Grade / divisor;
        if (!float.IsFinite(quantity) || quantity < 0 || (double)quantity > int.MaxValue) return false;
        var selected = (int)((long)context.AdditionalItemDraw * candidates.Count / 10_000);
        rewards.Add(new(candidates[selected], Math.Max(1, (int)quantity)));
        return true;
    }

    private static bool TryAdditionalBonus(EquipmentReinforcementDestroyBonusDefinition bonus,
        EquipmentDefinition equipment, byte oldLevel,
        List<EquipmentReinforcementFailureReward> rewards)
    {
        if (bonus.ItemId <= 0 || bonus.MinimumCorrectedGrade < 0 || bonus.UpgradeLevelBase < 0)
            return false;
        if (oldLevel <= bonus.UpgradeLevelBase || !bonus.WeightByRarity.TryGetValue(equipment.Rarity, out var weight))
            return true;
        var correction = bonus.GradeCorrectionByRarity.GetValueOrDefault(equipment.Rarity);
        if (correction < 0) return false;
        if (Math.Max(0, equipment.Grade - correction) < bonus.MinimumCorrectedGrade) return true;
        var distance = (double)oldLevel - bonus.UpgradeLevelBase;
        var raw = distance * distance * (float)weight;
        if (!double.IsFinite(raw) || raw < 0 || raw > int.MaxValue) return false;
        if ((int)raw > 0) rewards.Add(new(bonus.ItemId, (int)raw));
        return true;
    }
}
