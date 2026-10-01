using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEquipmentUpgradeRuleService
{
    ValueTask<EquipmentReinforcementQuote> QuoteTicketAsync(InventoryItemCoreState core, int materialItemId,
        CancellationToken ct, EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement) =>
        ValueTask.FromResult(new EquipmentReinforcementQuote(EquipmentReinforcementRejection.None));
    ValueTask<EquipmentReinforcementQuote> QuoteReinforcementAsync(InventoryItemCoreState core, CancellationToken ct,
        EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement);
    ValueTask<EquipmentOutworldDecision> DecideOutworldAsync(EquipmentOutworldAction action,
        int equipmentItemId, byte amplifyType, byte upgradeLevel, int materialItemId,
        byte selectedOption, int seed, CancellationToken ct);
    ValueTask<EquipmentForgingDecision> DecideForgingAsync(int equipmentItemId,
        ushort durability, byte oldLevel, int materialItemId, int roll, CancellationToken ct);
    /// <summary>Queries amplification admission without consuming assets.</summary>
    ValueTask<EquipmentAmplificationEligibility> CheckAmplificationAsync(InventoryItemCoreState core, CancellationToken ct);
}

public enum EquipmentAmplificationRejection
{
    None,
    UnsupportedContent,
    Forbidden,
    Sealed,
    UnidentifiedOption,
    NoOption,
    Durability,
}

public sealed record EquipmentAmplificationEligibility(EquipmentAmplificationRejection Rejection);
