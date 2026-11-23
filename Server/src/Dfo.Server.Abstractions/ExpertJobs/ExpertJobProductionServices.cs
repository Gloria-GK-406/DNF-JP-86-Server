using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.ExpertJobs;

public interface IExpertJobProductionService
{
    ValueTask<ExpertJobCraftResult> CraftAsync(SelectedSessionIdentity actor, int recipeId, ushort attempts,
        IEffectFootprintRecorder footprint, CancellationToken ct);
    ValueTask<ExpertJobExtractionResult> ExtractAsync(SelectedSessionIdentity actor, BusinessSlot extractor, BusinessSlot target,
        IEffectFootprintRecorder footprint, CancellationToken ct);
}

public interface IAlchemistDomainService : IExpertJobProductionService;
public interface IDollControllerDomainService : IExpertJobProductionService;
public interface IEnchanterDomainService : IExpertJobProductionService
{
    ValueTask<ExpertJobCraftResult> CraftBeadAsync(SelectedSessionIdentity actor, int recipeId, BusinessSlot card,
        IEffectFootprintRecorder footprint, CancellationToken ct);
    ValueTask<EnchanterStoreUseResult> EnchantAsync(ExpertStoreVisit visit, int recipeId, BusinessSlot card, BusinessSlot target,
        IEffectFootprintRecorder footprint, CancellationToken ct);
    ValueTask<ExpertMachineMaintenanceResult> RepairAsync(SelectedSessionIdentity actor, IEffectFootprintRecorder footprint,
        CancellationToken ct);
}

public enum EnchanterStoreRejection { None, InvalidItem, InvalidState, InsufficientGold, Exhausted }

public sealed record EnchanterStoreUseResult(EnchanterStoreRejection Rejection, EnchanterStoreUseReceipt? Receipt = null);

public sealed record EnchanterStoreUseReceipt(bool Succeeded, uint Experience, int Endurance, int GuestGold, int OwnerGold,
    IReadOnlyList<InventorySlotEffectChange> InventoryChanges, DateTimeOffset PreparedAt, bool ExperienceChanged,
    bool LevelChanged, ExpertStoreClosure? ClosedStore);

public sealed record EnchanterCardRecipe(byte Qualification, int RequiredLevel, int RequiredSkill,
    IReadOnlyList<RecipeAmount> Materials, IReadOnlyDictionary<int, int> BeadsByCardId);

public sealed record EnchanterCardRate(IReadOnlyList<int> SuccessByQualification, ExpertJobExperienceRange Experience);

public sealed record EnchanterDefinition(IReadOnlyDictionary<int, EnchanterCardRecipe> CardRecipes,
    IReadOnlyDictionary<int, EnchanterCardRate> CardRates, int WearMinimumLevel, int WearPerAttempt,
    IReadOnlyList<ExpertMachineRepairRule> Repairs);

public enum ExpertJobProductionRejection
{
    None,
    InvalidState,
    InvalidItem,
    InvalidRecipe,
    LevelTooLow,
    InsufficientMaterials,
    InsufficientGold,
    InventoryFull,
}

public sealed record ExpertJobStudyResult(ExpertJobProductionRejection Rejection, int RemainingCount = 0);

public sealed record ExpertJobCraftResult(ExpertJobProductionRejection Rejection, ExpertJobProductionReceipt? Receipt = null,
    RecipeAmount? AttemptedOutput = null, int Successes = 0, int Failures = 0, bool ExtractorProduct = false);

public sealed record ExpertJobExtractionResult(ExpertJobProductionRejection Rejection, ExpertJobProductionReceipt? Receipt = null);

public sealed record ExpertJobProductionReceipt(
    IReadOnlyList<InventorySlotEffectChange> InventoryChanges,
    IReadOnlyList<OpeningRewardDisplay> Rewards,
    DateTimeOffset PreparedAt,
    int? Gold,
    bool ExperienceChanged,
    bool LevelChanged)
{
    public IReadOnlyList<BusinessSlot> ReplacedSlots { get; init; } = [];
}

public sealed record ExpertJobRecipeDefinition(int RequiredLevel, int RequiredSkill, int RequiredSkillLevel,
    IReadOnlyList<RecipeAmount> Materials, RecipeAmount Product, int GoldCost, ExpertJobExperienceRange Experience);

public sealed record ExpertJobExperienceRange(int Minimum, int Maximum);

public sealed record ExpertJobCompoundRate(int MaximumLevelDifference, int SuccessPercent, ExpertJobExperienceRange Experience);

public sealed record ExpertJobExtractorDefinition(int RequiredLevel, ExpertJobExperienceRange Experience);

public sealed record ExpertJobExtractionRule(int ItemId, double Multiplier, int AdditionalTable, int GreatTable, int GreatChance);

public sealed record ExpertJobExtractionSelectionRule(
    int Table, int MinimumGrade, int MaximumGrade, int ItemId, int Weight, double Multiplier);

public sealed record ExpertJobProductionDefinition(
    IReadOnlyDictionary<int, ExpertJobRecipeDefinition> Recipes,
    IReadOnlyList<ExpertJobCompoundRate> CompoundRates,
    IReadOnlyDictionary<int, ExpertJobExtractorDefinition> Extractors,
    IReadOnlyDictionary<(int ExtractorId, int Rarity, int EquipmentState), ExpertJobExtractionRule> ExtractionResults,
    IReadOnlyList<ExpertJobExtractionSelectionRule> AdditionalResults,
    IReadOnlyList<ExpertJobExtractionSelectionRule> GreatResults,
    int? ExtractionSellGoldDivisor);
