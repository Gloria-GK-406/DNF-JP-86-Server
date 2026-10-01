using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed partial class EquipmentUpgradeRuleService
{
    public async ValueTask<EquipmentReinforcementQuote> QuoteTicketAsync(InventoryItemCoreState core, int materialItemId,
        CancellationToken ct, EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement)
    {
        var ticket = await definitions.GetTicketAsync(materialItemId, ct).ConfigureAwait(false);
        if (ticket is null) return new(EquipmentReinforcementRejection.None);
        if (kind is not (EquipmentUpgradeKind.Reinforcement or EquipmentUpgradeKind.Amplification) || ticket.Kind != kind)
            return new(EquipmentReinforcementRejection.CannotReinforce, IsTicket: true);
        if (ticket.ApplyMode != "fixed" || ticket.ApplyValue != -1 || ticket.TargetLevel > 31
            || ticket.SuccessRatePercent is < 0 or > 100)
            return new(EquipmentReinforcementRejection.UnsupportedContent, IsTicket: true);
        if (kind == EquipmentUpgradeKind.Amplification)
        {
            var eligibility = await CheckAmplificationAsync(core, ct).ConfigureAwait(false);
            if (eligibility.Rejection != EquipmentAmplificationRejection.None)
                return new(eligibility.Rejection == EquipmentAmplificationRejection.Durability
                    ? EquipmentReinforcementRejection.Durability : EquipmentReinforcementRejection.CannotReinforce, IsTicket: true);
        }
        else if (core.SealFlag != 0 || core.AmplifyType != 0)
            return new(EquipmentReinforcementRejection.CannotReinforce, IsTicket: true);
        var equipment = await equipmentDefinitions.GetEquipmentAsync(core.ItemId, ct).ConfigureAwait(false);
        if (equipment is null || WeightIndex(equipment.EquipmentType) is null or 1
            || equipment.ImpossibleContents.Contains("upgrade"))
            return new(EquipmentReinforcementRejection.CannotReinforce, IsTicket: true);
        if (equipment.MaxDurability is { } maximum && core.Durability != maximum)
            return new(EquipmentReinforcementRejection.Durability, IsTicket: true);
        return new(EquipmentReinforcementRejection.None, new TicketRule(materialItemId, ticket), true);
    }

    private sealed class TicketRule(int itemId, EquipmentUpgradeTicketDefinition ticket) : IEquipmentReinforcementRule
    {
        public byte? FixedTargetLevel => ticket.TargetLevel;
        public int GoldCost => 0;
        public int MaterialItemId => itemId;
        public int MaterialCount => 1;
        public bool CanDestroy => false;
        public bool CanUseAutomaticProtection => false;
        public ValueTask<EquipmentReinforcementProtectionDefinition?> ProtectionAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(null);
        public ValueTask<EquipmentProtectionChoice?> SelectAutomaticProtectionAsync(
            IReadOnlyList<EquipmentProtectionCandidate> candidates, CancellationToken ct) => ValueTask.FromResult<EquipmentProtectionChoice?>(null);
        public bool TryReinforce(int draw)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(draw);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(draw, 100_000);
            return draw < ticket.SuccessRatePercent * 1000;
        }
    }
}
