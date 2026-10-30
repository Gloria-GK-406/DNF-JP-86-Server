using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemUseEffectRuleService(IItemUseRuleService judgement) : IItemUseEffectRuleService
{
    public async ValueTask<ItemUseRule?> RuleForAsync(int itemId, CancellationToken ct)
    {
        var rule = await judgement.RuleForAsync(itemId, ct).ConfigureAwait(false);
        if (rule?.Action is "[love point coupon]" or "[upgrade wedding ring]" or "[repair coupon]")
            rule = rule with { HasUnreadableDeclaration = true };
        // A shop-listed MLA upgrade coupon must not fall through to a bare consumption.
        if (itemId == 490002848 && rule is not null)
            rule = rule with { HasUnreadableDeclaration = true };
        return rule;
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
