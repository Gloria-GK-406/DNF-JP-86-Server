using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class ExperienceItemDomainService(
    ICharacterDomainService characters, ICharacterItemUseDomainService itemUse,
    IExperienceItemRuleService rules,
    IItemUseEffectRuleService itemUseRules,
    IApplyEffectsDomainService effects,
    TimeProvider clock) : IExperienceItemDomainService
{
    public async ValueTask<ItemUseApplication?> TryUseAsync(
        SelectedSessionIdentity session,
        BusinessSlot source,
        ItemReference item,
        IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        var rule = await rules.RuleForAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        if (rule is null) return null;
        if (!rule.IsReadable)
            return new(ItemUsability.NotAUsableItem, item.ItemId, item.Count);

        var ordinaryRule = await itemUseRules.RuleForAsync(item.ItemId, cancellationToken)
            .ConfigureAwait(false);
        var character = characters.Get(session.CharacterId);
        var facts = ordinaryRule is null || !ordinaryRule.IsReadable
            ? null
            : await itemUse.FactsAsync(session.CharacterId,
                itemUseRules.CooldownKeyFor(item.ItemId, ordinaryRule), cancellationToken)
                .ConfigureAwait(false);
        var at = clock.GetUtcNow();
        var ordinaryPlan = itemUseRules.Build(
            ordinaryRule, source, item, facts, currentPetUid: null, at);
        if (ordinaryPlan.Status != ItemUsability.Allowed)
            return new(ordinaryPlan.Status, item.ItemId, item.Count);

        var progress = await character.ProgressAsync(cancellationToken).ConfigureAwait(false);
        if (progress is null)
            return new(ItemUsability.CharacterStateUnavailable, item.ItemId, item.Count);
        var decision = rules.Decide(rule, progress);
        if (decision.Status != ItemUsability.Allowed || decision.Effect is null)
            return new(decision.Status, item.ItemId, item.Count);

        var plan = ordinaryPlan.Effects.Append(decision.Effect).ToArray();
        var applied = await effects.ApplyAsync(
            new(session.AccountId, session.CharacterId, at, footprint, session), plan, cancellationToken)
            .ConfigureAwait(false);
        var award = applied.Changes<CharacterExperienceChange>().Single().Award;
        var points = await character.RemainingSkillPointsAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Experience item skill-point projection is unavailable.");
        var remaining = applied.Changes<ContainerConsumptionChange>()
            .LastOrDefault(change => change.Slot == source)?.RemainingCount ?? item.Count;
        return new(ItemUsability.Allowed, item.ItemId, remaining, applied)
        {
            ExperienceAward = award,
            SkillPoints = points,
        };
    }
}
