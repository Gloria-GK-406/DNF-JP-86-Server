using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record EquipmentReinforcementQuote(EquipmentReinforcementRejection Rejection,
    IEquipmentReinforcementRule? Rule = null);

public sealed record EquipmentProtectionCandidate(BusinessSlot Slot, int ItemId);

public sealed record EquipmentProtectionChoice(BusinessSlot Slot,
    EquipmentReinforcementProtectionDefinition Definition);

public sealed record EquipmentReinforcementFailureContext(
    EquipmentReinforcementProtectionDefinition? Protection, int AdditionalItemDraw, int GreatDraw);

public sealed record EquipmentReinforcementFailureReward(int ItemId, int Count);

public sealed record EquipmentReinforcementFailureResult(EquipmentReinforcementRejection Rejection,
    EquipmentReinforcementOutcome Outcome, byte NewLevel, bool ConsumeProtection,
    IReadOnlyList<EquipmentReinforcementFailureReward> Rewards);

/// <summary>Plans failure consequences from immutable facts; it cannot mutate an instance.</summary>
public interface IEquipmentReinforcementFailureRuleService
{
    ValueTask<EquipmentReinforcementFailureResult> GetResultAsync(EquipmentInstanceFacts equipment,
        EquipmentReinforcementFailureContext context, CancellationToken ct);
}

public static class EquipmentReinforcementFailureRuleExtensions
{
    public static async ValueTask<EquipmentReinforcementFailureResult> GetResultAsync(
        this IEquipmentReinforcementFailureRuleService rules, IEquipmentInstance equipment,
        EquipmentReinforcementFailureContext context, CancellationToken ct) =>
        await rules.GetResultAsync(await equipment.ReadAsync(ct).ConfigureAwait(false), context, ct)
            .ConfigureAwait(false);
}

public interface IEquipmentReinforcementRule
{
    int GoldCost { get; }
    int MaterialItemId { get; }
    int MaterialCount { get; }
    bool CanDestroy { get; }
    ValueTask<EquipmentReinforcementProtectionDefinition?> ProtectionAsync(int itemId, CancellationToken ct);
    ValueTask<EquipmentProtectionChoice?> SelectAutomaticProtectionAsync(
        IReadOnlyList<EquipmentProtectionCandidate> candidates, CancellationToken ct);
    bool TryReinforce(int draw);
}

public interface IEquipmentEnchantRuleService
{
    ValueTask<int?> CardAsync(InventoryItemCoreState bead, int equipmentItemId, CancellationToken ct);
}

public sealed record EquipmentRepairQuote(int Cost, ushort Durability, bool Write);

public interface IEquipmentRepairRule
{
    ValueTask<EquipmentRepairQuote?> QuoteAsync(InventoryItemCoreState core, CancellationToken ct);
}

public interface IEquipmentRepairRuleService
{
    ValueTask<IEquipmentRepairRule?> PrepareAsync(CancellationToken ct);
    ValueTask<ushort?> AutoRepairDurabilityAsync(InventoryItemCoreState core, CancellationToken ct);
}
