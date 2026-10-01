using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentEffectPlanRuleService(IEquipmentReinforcementFailureRuleService failures,
    IEquipmentEnchantRuleService enchant, IEquipmentUpgradeRuleService upgrades,
    IEquipmentMagicSealRuleService seals, IEquipmentDisjointRuleService disjoint,
    IEquipmentDisjointDefinitionSource disjointContent, IAccountMaterialRuleService accounts) : IEquipmentEffectPlanRuleService
{
    public async ValueTask<EquipmentEffectPlan<EquipmentReinforcementFailureResult>> ReinforcementAsync(EquipmentInstanceFacts target,
        IEquipmentReinforcementRule rule, MaterialCost material, MaterialCost? protection,
        EquipmentReinforcementFailureContext failure, bool succeeded, CancellationToken ct)
    {
        var decision = rule.FixedTargetLevel is { } fixedLevel
            ? new EquipmentReinforcementFailureResult(EquipmentReinforcementRejection.None,
                succeeded ? EquipmentReinforcementOutcome.Succeeded : EquipmentReinforcementOutcome.Retained,
                succeeded ? fixedLevel : target.Core.ReinforcementLevel, false, [])
            : succeeded
            ? new EquipmentReinforcementFailureResult(EquipmentReinforcementRejection.None, EquipmentReinforcementOutcome.Succeeded,
                checked((byte)(target.Core.ReinforcementLevel + 1)), false, [])
            : await failures.GetResultAsync(target, failure, ct).ConfigureAwait(false);
        if (decision.Rejection != EquipmentReinforcementRejection.None) return new(decision, []);
        var effects = new List<DomainEffect> { Consume(material) };
        if (rule.FixedTargetLevel is null) effects.Add(new SpendGoldEffect(rule.GoldCost));
        if (decision.ConsumeProtection) effects.Add(Consume(protection
            ?? throw new InvalidOperationException("Protected failure lacks its protection target.")));
        if (rule.FixedTargetLevel is null || succeeded)
            effects.Add(decision.Outcome == EquipmentReinforcementOutcome.Destroyed
                ? new DestroyEquipmentEffect(Target(target)) : new SetEquipmentUpgradeEffect(Target(target), decision.NewLevel));
        foreach (var reward in decision.Rewards) effects.Add(Grant(reward.ItemId, reward.Count));
        return new(decision, effects.AsReadOnly());
    }

    public async ValueTask<EquipmentEffectPlan<int?>> EnchantAsync(EquipmentInstanceFacts target, BusinessSlot source,
        InventoryItemCoreState bead, CancellationToken ct)
    {
        var card = await enchant.CardAsync(bead, target.Core.ItemId, ct).ConfigureAwait(false);
        return new(card, card is null ? [] : Array.AsReadOnly<DomainEffect>([
            new ConsumeFromContainerEffect(source, bead.ItemId, 1), new SetEquipmentEnchantmentEffect(Target(target), card.Value)]));
    }

    public async ValueTask<EquipmentEffectPlan<EquipmentOutworldDecision>> OutworldAsync(EquipmentInstanceFacts target,
        EquipmentOutworldAction action, BusinessSlot source, int materialId, byte selectedOption, int seed, CancellationToken ct)
    {
        var decision = await upgrades.DecideOutworldAsync(action, target.Core.ItemId, target.Core.AmplifyType,
            target.Core.ReinforcementLevel, materialId, selectedOption, seed, ct).ConfigureAwait(false);
        return new(decision, decision.Rejection != EquipmentOutworldRejection.None ? [] : Array.AsReadOnly<DomainEffect>([
            new ConsumeFromContainerEffect(source, materialId, decision.MaterialCount),
            new SetEquipmentDimensionalEffect(Target(target), decision.AmplifyType, decision.AmplifyValue)]));
    }

    public async ValueTask<EquipmentEffectPlan<EquipmentMagicSealRuleDecision>> MagicSealAsync(EquipmentInstanceFacts target,
        byte? changedIndex, int seed, CancellationToken ct)
    {
        var decision = await seals.DecideAsync(target.Core.ItemId,
            [target.Core.RandomOption0, target.Core.RandomOption1, target.Core.RandomOption2], changedIndex, seed, ct).ConfigureAwait(false);
        if (decision.Rejection != EquipmentMagicSealRejection.None) return new(decision, []);
        var options = decision.Options ?? throw new InvalidOperationException("Magic seal plan lacks options.");
        return new(decision, Array.AsReadOnly<DomainEffect>([new SpendGoldEffect(decision.GoldCost),
            new SetEquipmentMagicSealEffect(Target(target), Array.AsReadOnly(options.ToArray()))]));
    }

    public async ValueTask<EquipmentEffectPlan<EquipmentForgingDecision>> ForgingAsync(EquipmentInstanceFacts target,
        BusinessSlot source, int materialId, int draw, CancellationToken ct)
    {
        var decision = await upgrades.DecideForgingAsync(target.Core.ItemId, target.Core.Durability,
            target.Core.GenuineUpgrade, materialId, draw, ct).ConfigureAwait(false);
        if (decision.Rejection != EquipmentForgingRejection.None) return new(decision, []);
        var effects = new List<DomainEffect> { new ConsumeFromContainerEffect(source, materialId, decision.MaterialCount) };
        if (decision.Succeeded) effects.Add(new SetEquipmentForgingEffect(Target(target), decision.NewLevel));
        return new(decision, effects.AsReadOnly());
    }

    public async ValueTask<EquipmentEffectPlan<EquipmentDisjointRuleResult>> DisjointAsync(EquipmentInstanceFacts target,
        MaterialCost? machine, int seed, CancellationToken ct)
    {
        if (machine is not null)
        {
            var definition = await disjointContent.GetPortableMachineAsync(machine.ItemId, ct).ConfigureAwait(false);
            if (definition is null) return new(new(EquipmentDisjointRejection.UnsupportedMethod), []);
            var equipment = await disjointContent.GetEquipmentAsync(target.Core.ItemId, ct).ConfigureAwait(false);
            if (equipment is null || equipment.MinimumLevel > definition.MaximumEquipmentLevel)
                return new(new(EquipmentDisjointRejection.CannotDisjoint), []);
        }
        var decision = await disjoint.ResolveAsync(new(target.Core.ItemId, target.Core), EquipmentDisjointSettings.System, seed, ct)
            .ConfigureAwait(false);
        if (decision.Rejection != EquipmentDisjointRejection.None || decision.Rewards is not { Count: > 0 })
            return new(decision with { Rejection = decision.Rejection == EquipmentDisjointRejection.None
                ? EquipmentDisjointRejection.CannotDisjoint : decision.Rejection }, []);
        var effects = new List<DomainEffect>();
        if (machine is not null) effects.Add(Consume(machine));
        effects.Add(new DestroyEquipmentEffect(Target(target)));
        foreach (var reward in decision.Rewards) effects.Add(Grant(reward.ItemId, reward.Count));
        return new(decision, effects.AsReadOnly());
    }

    private DomainEffect Grant(int itemId, int count) => accounts.TryGetByItemId(itemId, out var definition)
        ? new GrantAccountMaterialEffect(definition.Kind, itemId, count) : new GrantStackableEffect(itemId, count);
    private static DomainEffect Consume(MaterialCost cost) => cost.Location switch
    {
        MaterialLocation.Carried carried => new ConsumeFromContainerEffect(carried.Slot, cost.ItemId, cost.Count),
        MaterialLocation.Account account => new ConsumeAccountMaterialEffect(account.Kind, cost.ItemId, cost.Count),
        _ => throw new ArgumentOutOfRangeException(nameof(cost)),
    };
    private static EquipmentEffectTarget Target(EquipmentInstanceFacts target) => new(new(target.ItemUid), target.Core.ItemId);
}
