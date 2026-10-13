using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed partial class EquipmentUpgradeRuleService
{
    public async ValueTask<EquipmentReinforcementQuote> QuoteTicketAsync(ItemSnapshot core, int materialItemId,
        CancellationToken ct, EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement)
    {
        var ticket = await definitions.GetTicketAsync(materialItemId, ct).ConfigureAwait(false);
        if (ticket is null) return new(EquipmentReinforcementRejection.None);
        var random = ticket.ApplyMode == "random" && ticket.RandomLevels.Count > 0;
        if (kind is not (EquipmentUpgradeKind.Reinforcement or EquipmentUpgradeKind.Amplification) || ticket.Kind != kind)
            return new(EquipmentReinforcementRejection.CannotReinforce, IsTicket: true);
        if ((!random && ticket.ApplyMode != "fixed") || ticket.ApplyValue != -1 || ticket.TargetLevel > 31
            || ticket.RequiresAdditionalMaterials
            || ticket.SuccessRatePercent is < 0 or > 100)
            return new(EquipmentReinforcementRejection.UnsupportedContent, IsTicket: true);
        if (kind == EquipmentUpgradeKind.Amplification)
        {
            var eligibility = await CheckAmplificationAsync(core, ct).ConfigureAwait(false);
            if (eligibility.Rejection != EquipmentAmplificationRejection.None)
                return new(eligibility.Rejection == EquipmentAmplificationRejection.Durability
                    ? EquipmentReinforcementRejection.Durability : EquipmentReinforcementRejection.CannotReinforce, IsTicket: true);
        }
        else if ((!random && core.SealFlag != 0) || core.AmplifyType != 0)
            return new(EquipmentReinforcementRejection.CannotReinforce, IsTicket: true);
        var equipment = await equipmentDefinitions.GetEquipmentAsync(core.ItemId, ct).ConfigureAwait(false);
        if (equipment is null || WeightIndex(equipment.EquipmentType) is null or 1
            || equipment.ImpossibleContents.Contains("upgrade"))
            return new(EquipmentReinforcementRejection.CannotReinforce, IsTicket: true);
        if (equipment.MaxDurability is { } maximum && core.Durability != maximum)
            return new(EquipmentReinforcementRejection.Durability, IsTicket: true);
        if (random && (!ticket.AllowedRarities.Contains(equipment.Rarity)
            || ticket.SealRestriction == 1 && core.SealFlag == 0 || ticket.SealRestriction == 2 && core.SealFlag != 0
            || !(ticket.SlotRestriction switch
            {
                0 => true,
                1 => equipment.EquipmentType == "weapon",
                2 => WeightIndex(equipment.EquipmentType) is >= 2 and <= 6,
                3 => WeightIndex(equipment.EquipmentType) is >= 7 and <= 9,
                _ => false,
            }))) return new(EquipmentReinforcementRejection.CannotReinforce, IsTicket: true);
        return new(EquipmentReinforcementRejection.None,
            random ? new RandomTicketRule(materialItemId, ticket) : new TicketRule(materialItemId, ticket), true);
    }

    private class TicketRule(int itemId, EquipmentUpgradeTicketDefinition ticket, int rollUpperBound = 100_000) : IEquipmentReinforcementRule
    {
        protected EquipmentUpgradeTicketDefinition Definition => ticket;
        public virtual byte? FixedTargetLevel => ticket.TargetLevel;
        public virtual int RollUpperBound => rollUpperBound;
        public virtual IEquipmentReinforcementRule ResolveRoll(int draw) => this;
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
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(draw, RollUpperBound);
            return draw < (long)ticket.SuccessRatePercent * RollUpperBound / 100;
        }
    }

    private sealed class RandomTicketRule(int itemId, EquipmentUpgradeTicketDefinition ticket) : TicketRule(itemId, ticket)
    {
        public override byte? FixedTargetLevel => null;
        public override int RollUpperBound => checked((int)Definition.RandomLevels.Sum(value => (long)value.Weight));
        public override IEquipmentReinforcementRule ResolveRoll(int draw)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(draw);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(draw, RollUpperBound);
            long cursor = 0;
            foreach (var row in Definition.RandomLevels)
            {
                cursor += row.Weight;
                if (draw < cursor) return new TicketRule(MaterialItemId,
                    Definition with { TargetLevel = row.Level, SuccessRatePercent = 100 }, RollUpperBound);
            }
            throw new InvalidDataException("Random upgrade ticket has no weighted outcome.");
        }
    }
}
