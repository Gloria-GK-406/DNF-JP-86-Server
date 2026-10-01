using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Content;

/// <summary>One 19-column row from an A21 reinforcement or amplification upgrade table.</summary>
public sealed record EquipmentReinforcementRow(
    int TargetLevel,
    IReadOnlyList<double> Columns,
    int FailureThreshold,
    int PenaltyType,
    int PenaltyValue,
    int MaterialItemId,
    int MaterialCount);

/// <summary>Archive declarations used to plan, but not commit, an equipment upgrade.</summary>
public sealed record EquipmentReinforcementTable(
    IReadOnlyList<EquipmentReinforcementRow> Rows,
    IReadOnlyList<int> BaseGoldByEquipmentLevel,
    IReadOnlyList<double> CostWeightsByRarity,
    IReadOnlyList<double> CostWeightsByEquipmentType,
    IReadOnlyDictionary<int, double> CostWeightByCurrentLevel,
    IReadOnlyDictionary<int, int> DestroyLevelByRarity,
    IReadOnlyDictionary<int, int> MaxUpgradeLevelByRarity,
    EquipmentReinforcementDestroyBonusDefinition DestroyBonus,
    int NoticeLevel);

/// <summary>Only explicitly declared rarity keys are present in these maps.</summary>
public sealed record EquipmentReinforcementDestroyBonusDefinition(
    int ItemId,
    int MinimumCorrectedGrade,
    int UpgradeLevelBase,
    IReadOnlyDictionary<int, double> WeightByRarity,
    IReadOnlyDictionary<int, int> GradeCorrectionByRarity);

public enum EquipmentReinforcementProtectionKind
{
    ResetToZero,
    RetainLevel,
}

/// <summary>Action declaration only; the caller must separately validate ownership and expiry.</summary>
public sealed record EquipmentReinforcementProtectionDefinition(
    EquipmentReinforcementProtectionKind Kind,
    int RetainedLevel);

public interface IEquipmentReinforcementDefinitionSource
{
    ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken cancellationToken);

    ValueTask<EquipmentReinforcementTable?> GetAmplificationTableAsync(CancellationToken cancellationToken);

    ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(
        int itemId, CancellationToken cancellationToken,
        EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement);
}
