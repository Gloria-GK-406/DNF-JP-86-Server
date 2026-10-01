using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEquipmentUpgradeRuleService
{
    ValueTask<EquipmentReinforcementQuote> QuoteReinforcementAsync(InventoryItemCoreState core, CancellationToken ct);
    ValueTask<EquipmentOutworldDecision> DecideOutworldAsync(EquipmentOutworldAction action,
        int equipmentItemId, byte amplifyType, byte upgradeLevel, int materialItemId,
        byte selectedOption, int seed, CancellationToken ct);
    ValueTask<EquipmentForgingDecision> DecideForgingAsync(int equipmentItemId,
        ushort durability, byte oldLevel, int materialItemId, int roll, CancellationToken ct);
    /// <summary>Basic equipment eligibility only; amplification costs and the upgrade transaction are not implemented.</summary>
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
