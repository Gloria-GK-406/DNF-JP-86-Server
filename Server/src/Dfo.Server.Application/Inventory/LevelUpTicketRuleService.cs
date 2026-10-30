using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Quests;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class LevelUpTicketRuleService(IStackableActionDefinitionSource actions,
    IItemUseRuleService uses, IQuestContentRuleService quests, IExperienceRuleService experience,
    ISpecialConsumableEffectPlanRuleService consumables) : ILevelUpTicketRuleService
{
    public async ValueTask<LevelUpTicketPlan> PlanAsync(BusinessSlot source, ItemReference item,
        CharacterIdentityState identity, CharacterProgressionState progress, bool hardcore,
        QuestStateFacts state, DateTimeOffset at, CancellationToken ct)
    {
        var action = await actions.GetAsync(item.ItemId, ct).ConfigureAwait(false);
        if (action is not { IsReadable: true, Action: "[level up ticket]", LevelIncrease: > 0 })
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Not a supported level-up ticket.");
        var scale = experience.CharacterScale();
        if (progress.Level < Math.Max(1, action.MinimumLevel) || progress.Level > action.MaximumLevel
            || progress.Level >= scale.TopLevel || hardcore && action.BlockedInHardcore
            || action.ExpiresAtUnixSeconds > 0 && at.ToUnixTimeSeconds() >= action.ExpiresAtUnixSeconds)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Ticket level, mode or expiration restriction.");
        var target = (byte)Math.Min(scale.TopLevel, (long)progress.Level + action.LevelIncrease);
        var all = await quests.AllAsync(ct).ConfigureAwait(false);
        var cleared = state.Cleared.Select(x => new ClearedQuest(x.QuestId, x.Mark)).ToArray();
        // Match the upstream stage boundary, excluding event, hidden and unnamed stories.
        var eligible = all.Where(q => q.StoryStageLevel is { } stage && stage < target
            && q.IsExposedByNpc != false && q.IsEventOnly != true).ToArray();
        var selected = QuestClearCouponRuleService.Select(eligible, (byte)(target - 1), identity.Job, identity.GrowType, cleared);
        ImmutableArray<DomainEffect> effects;
        ImmutableArray<int> completed;
        if (selected.Count > 0)
        {
            var plan = await consumables.CouponAsync(source, item, state, selected, at, ct).ConfigureAwait(false);
            effects = plan.Effects;
            completed = plan.Completed;
        }
        else
        {
            var rule = await uses.RuleForAsync(item.ItemId, ct).ConfigureAwait(false);
            if (rule is not { IsReadable: true }) throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Unknown item-use rule.");
            effects = [new ConsumeFromContainerEffect(source, item.ItemId, 1)];
            if (rule.CooldownMilliseconds > 0)
                effects = effects.Add(new StartItemCooldownEffect(uses.CooldownKeyFor(item.ItemId, rule), TimeSpan.FromMilliseconds(rule.CooldownMilliseconds)));
            completed = [];
        }
        return new(target, completed, effects.Add(new SetCharacterLevelEffect(progress, target)));
    }
}
