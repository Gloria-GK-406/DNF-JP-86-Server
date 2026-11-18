using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterSkillRefundService(
    ICharacterBusinessInventoryDomainService businessInventories,
    IOpeningInventoryDomainService inventory,
    ICharacterSkillResetRuleService resetRules,
    TimeProvider clock,
    ISpecialConsumableEffectPlanRuleService effectPlans)
{
    public async ValueTask<CharacterSkillLearningResult> ApplyAsync(SelectedSessionIdentity session,
        SkillGroup group, SkillBuildId build, IReadOnlyList<CharacterSkillLearningChange> changes,
        CharacterSkillFacts facts, IReadOnlyList<CharacterSkillState> current, int learningLevel,
        IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        var decision = effectPlans.RefundSkills(facts, build.Value, changes, learningLevel);
        if (decision.Rejection != CharacterSkillLearningRejection.None)
            return new(decision.Rejection);
        var at = clock.GetUtcNow();
        var itemStore = businessInventories.GetStore(session.CharacterId, BusinessInventoryStoreKind.Item);
        foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable })
        {
            var bag = itemStore.FindContainer(space);
            if (bag is null)
                continue;
            var water = (await bag.GetAllAsync(ct).ConfigureAwait(false))
                .Where(item => item.IsPile && item.ItemId == resetRules.ConsumableItemId && item.Count > 0
                    && !item.Snapshot.Lifetime.IsExpired(at))
                .OrderBy(item => item.SlotIndex).FirstOrDefault();
            if (water is null)
                continue;

            var slot = new BusinessSlot(space, water.SlotIndex);
            var source = await inventory.SourceAsync(session, slot, at, ct).ConfigureAwait(false);
            var effects = await effectPlans.RefundAsync(slot, source, group, build.Value, current, decision.Changed, ct)
                .ConfigureAwait(false);
            var consumed = (await inventory.ApplyAsync(session, effects, at, footprint, ct).ConfigureAwait(false)).Inventory;
            return new(CharacterSkillLearningRejection.None, new(session, build.Value, decision.Changed, decision.Ledger!, consumed));
        }
        return new(CharacterSkillLearningRejection.ResetItemRequired);
    }
}
