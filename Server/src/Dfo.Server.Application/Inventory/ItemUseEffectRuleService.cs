using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Quests;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemUseEffectRuleService(IItemUseRuleService judgement, IStackableActionDefinitionSource stackableActions) : IItemUseEffectRuleService
{
    internal const string RentPointAction = "[rent point]";

    public async ValueTask<ItemUseRule?> RuleForAsync(int itemId, CancellationToken ct)
    {
        var rule = await judgement.RuleForAsync(itemId, ct).ConfigureAwait(false);
        if (rule?.Action is "[love point coupon]" or "[upgrade wedding ring]" or "[repair coupon]" or "[repair equipment]")
            rule = rule with { HasUnreadableDeclaration = true };
        // These tickets have dedicated settlement; ordinary use would only spend them.
        if (rule is not null && (itemId is 490002848 or AuraSkinSlotRuleService.ActionTicketItemId
            or QuestClearCouponRuleService.SideCouponItemId || AbyssQuestClearTicketRuleService.IsTicket(itemId)))
            rule = rule with { HasUnreadableDeclaration = true };
        return rule;
    }

    public async ValueTask<ItemUseRule?> LuckyStarActionRuleAsync(int itemId, CancellationToken ct) =>
        itemId == 10002998 && await RuleForAsync(itemId, ct).ConfigureAwait(false) is { Action: RentPointAction } rule
            ? rule : null;

    public async ValueTask<int?> GuildExtensionCostAsync(int itemId, BusinessSlot source, ItemUsePlan plan, CancellationToken ct)
    {
        var action = await stackableActions.GetAsync(itemId, ct).ConfigureAwait(false);
        return action is { Action: GuildDungeonExtensionTicket.Action, IsReadable: true, GuildDungeonExtensionCost: > 0 }
            && plan.Effects.Any(effect => effect is ConsumeFromContainerEffect { Count: 1 } consume
                && consume.Slot == source && consume.ExpectedItemId == itemId)
            ? action.GuildDungeonExtensionCost : null;
    }

    public ItemCooldownKey CooldownKeyFor(int itemId, ItemUseRule rule) => judgement.CooldownKeyFor(itemId, rule);
    public ItemUsePlan Build(ItemUseRule? rule, BusinessSlot source, ItemReference item,
        ItemUseCandidate? character, int? currentPetUid, DateTimeOffset at)
    {
        if (rule is null || !rule.IsReadable || item.Count <= 0 || !item.IsPile || source.Index != item.SlotIndex
            || item.Snapshot.Lifetime.IsExpired(at)
            || source.Space == BusinessInventorySpace.CreatureConsumable && item.ItemId != 24)
            return new(ItemUsability.NotAUsableItem, []);
        if (character is null) return new(ItemUsability.CharacterStateUnavailable, []);
        var verdict = judgement.Judge(rule, character);
        if (verdict != ItemUsability.Allowed) return new(verdict, []);
        if (item.ItemId == 24 && currentPetUid is null) return new(ItemUsability.PlaceNotAllowed, []);
        var effects = new List<DomainEffect>();
        if (rule.IsConsumedOnUse || item.ItemId == 24) effects.Add(new ConsumeFromContainerEffect(source, item.ItemId, 1));
        if (rule.CooldownMilliseconds > 0)
            effects.Add(new StartItemCooldownEffect(CooldownKeyFor(item.ItemId, rule), TimeSpan.FromMilliseconds(rule.CooldownMilliseconds)));
        if (rule.ExpBonusRate > 0 && rule.EffectDurationMilliseconds > 0)
            effects.Add(new StartSpiritPotionEffect(rule.ExpBonusRate, TimeSpan.FromMilliseconds(rule.EffectDurationMilliseconds)));
        if (item.ItemId == 24) effects.Add(new AddPetStomachEffect(currentPetUid!.Value, 30));
        return new(ItemUsability.Allowed, effects.AsReadOnly());
    }
}
