using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record EquipmentReinforcementQuote(EquipmentReinforcementRejection Rejection,
    IEquipmentReinforcementRule? Rule = null, bool IsTicket = false);

public sealed record EquipmentProtectionCandidate(BusinessSlot Slot, int ItemId);

public sealed record EquipmentProtectionChoice(BusinessSlot Slot,
    EquipmentReinforcementProtectionDefinition Definition);

public sealed record EquipmentReinforcementFailureContext(
    EquipmentReinforcementProtectionDefinition? Protection, int AdditionalItemDraw, int GreatDraw,
    EquipmentUpgradeKind Kind = EquipmentUpgradeKind.Reinforcement);

public sealed record EquipmentReinforcementFailureReward(int ItemId, int Count);

public sealed record EquipmentReinforcementFailureResult(EquipmentReinforcementRejection Rejection,
    EquipmentReinforcementOutcome Outcome, byte NewLevel, bool ConsumeProtection,
    IReadOnlyList<EquipmentReinforcementFailureReward> Rewards);

/// <summary>Plans failure consequences from immutable facts; it cannot mutate an instance.</summary>
public interface IEquipmentReinforcementFailureRuleService
{
    ValueTask<EquipmentReinforcementFailureResult> GetResultAsync(ItemReference equipment,
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
    int RollUpperBound => 100_000;
    /// <summary>Returns an immutable, resolved target for a weighted ticket; ordinary rules keep themselves.</summary>
    IEquipmentReinforcementRule ResolveRoll(int draw) => this;
    /// <summary>Null for NPC +1; a declared level for a fixed-level ticket.</summary>
    byte? FixedTargetLevel => null;
    int GoldCost { get; }
    int MaterialItemId { get; }
    int MaterialCount { get; }
    bool CanDestroy { get; }
    bool CanUseAutomaticProtection { get; }
    ValueTask<EquipmentReinforcementProtectionDefinition?> ProtectionAsync(int itemId, CancellationToken ct);
    ValueTask<EquipmentProtectionChoice?> SelectAutomaticProtectionAsync(
        IReadOnlyList<EquipmentProtectionCandidate> candidates, CancellationToken ct);
    bool TryReinforce(int draw);
}

public interface IEquipmentEnchantRuleService
{
    ValueTask<int?> CardAsync(ItemSnapshot bead, int equipmentItemId, CancellationToken ct);
}

public sealed record EquipmentRepairQuote(int Cost, ushort Durability, bool Write);

public interface IEquipmentRepairRule
{
    ValueTask<EquipmentRepairQuote?> QuoteAsync(ItemSnapshot core, CancellationToken ct);
}

public interface IEquipmentRepairRuleService
{
    ValueTask<IEquipmentRepairCouponRule?> PrepareCouponAsync(int itemId, CancellationToken ct);
    ValueTask<IEquipmentRepairRule?> PrepareAsync(CancellationToken ct);
    ValueTask<ushort?> AutoRepairDurabilityAsync(ItemSnapshot core, CancellationToken ct);
}

public interface IEquipmentRepairCouponRule
{
    ItemCooldownKey CooldownKey { get; }
    TimeSpan Cooldown { get; }
    bool Allows(Dfo.Server.Abstractions.Characters.ItemUseCandidate candidate, int? dungeonId);
}
