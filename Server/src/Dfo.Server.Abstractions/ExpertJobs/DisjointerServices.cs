using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.ExpertJobs;

public interface IDisjointerDomainService
{
    ValueTask<DisjointMachineUseResult> DisjointAsync(ExpertStoreVisit visit, BusinessSlot slot,
        IEffectFootprintRecorder footprint, CancellationToken ct);
    ValueTask<ExpertMachineMaintenanceResult> RepairAsync(SelectedSessionIdentity owner,
        IEffectFootprintRecorder footprint, CancellationToken ct);
    ValueTask<ExpertMachineMaintenanceResult> UpgradeAsync(SelectedSessionIdentity owner,
        IEffectFootprintRecorder footprint, CancellationToken ct);
}

public sealed record ExpertMachineRepairRule(int FullCost, int MaximumEndurance);

public sealed record DisjointMachineDefinition(
    double BaseDivisor,
    int MinimumWear,
    int MaximumWear,
    int MinimumExperience,
    int MaximumExperience,
    int SelfServiceChance,
    int SelfServiceItemId,
    int SelfServiceCount,
    IReadOnlyDictionary<(int Grade, int Rarity, int EquipmentState), DisjointMachineMaterialRule> Results,
    IReadOnlyList<DisjointMachineSelectionRule> AdditionalResults,
    IReadOnlyList<DisjointMachineSelectionRule> GreatResults,
    IReadOnlyList<ExpertMachineRepairRule> Repairs,
    IReadOnlyDictionary<int, int> UpgradeCosts,
    IReadOnlyDictionary<int, int> MinimumCharacterLevels);

public sealed record DisjointMachineMaterialRule(
    int ItemId, double Multiplier, int AdditionalTable, int GreatTable, int GreatChance);

public sealed record DisjointMachineSelectionRule(
    int Table, int MinimumGrade, int MaximumGrade, int ItemId, int Weight, double Divisor);

public enum DisjointMachineRejection
{
    None,
    InvalidStore,
    InvalidItem,
    InventoryFull,
    InsufficientGold,
    GradeTooLow,
    Exhausted,
}

public sealed record DisjointMachineUseResult(
    DisjointMachineRejection Rejection,
    DisjointMachineUseReceipt? Receipt = null);

public sealed record DisjointMachineUseReceipt(
    IReadOnlyList<OpeningRewardDisplay> Rewards,
    IReadOnlyList<InventorySlotEffectChange> InventoryChanges,
    DateTimeOffset PreparedAt,
    int RequesterGold,
    int OwnerGold,
    int Endurance,
    bool ExperienceChanged,
    ExpertStoreClosure? ClosedStore);

public enum ExpertMachineMaintenanceRejection
{
    None,
    InvalidState,
    CannotRepair,
    CannotUpgrade,
    CharacterLevelTooLow,
    InsufficientGold,
}

public sealed record ExpertMachineMaintenanceResult(
    ExpertMachineMaintenanceRejection Rejection,
    int Gold = 0,
    int Endurance = 0,
    int Grade = 0);
