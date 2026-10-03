using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record EquipmentEffectPlan<TDecision>(TDecision Decision, IReadOnlyList<DomainEffect> Effects);
public interface IEquipmentEffectPlanRuleService
{
    ValueTask<EquipmentEffectPlan<EquipmentReinforcementFailureResult>> ReinforcementAsync(ItemReference target,
        IEquipmentReinforcementRule rule, MaterialCost material, MaterialCost? protection,
        EquipmentReinforcementFailureContext failure, bool succeeded, CancellationToken ct);
    ValueTask<EquipmentEffectPlan<int?>> EnchantAsync(ItemReference target, BusinessSlot source,
        ItemSnapshot bead, CancellationToken ct);
    ValueTask<EquipmentEffectPlan<EquipmentOutworldDecision>> OutworldAsync(ItemReference target,
        EquipmentOutworldAction action, BusinessSlot source, int materialId, byte selectedOption, int seed, CancellationToken ct);
    ValueTask<EquipmentEffectPlan<EquipmentMagicSealRuleDecision>> MagicSealAsync(ItemReference target,
        byte? changedIndex, int seed, CancellationToken ct);
    ValueTask<EquipmentEffectPlan<EquipmentForgingDecision>> ForgingAsync(ItemReference target,
        BusinessSlot source, int materialId, int draw, CancellationToken ct);
    ValueTask<EquipmentEffectPlan<EquipmentDisjointRuleResult>> DisjointAsync(ItemReference target,
        MaterialCost? machine, int seed, CancellationToken ct);
}
