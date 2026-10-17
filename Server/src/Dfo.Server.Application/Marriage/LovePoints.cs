using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed class MarriageLovePointRuleService(IMarriageCeremonyContentSource content, IItemUseJudgementService judgement) : IMarriageLovePointRuleService
{
    public async ValueTask<MarriageLovePointItemRule?> RuleForAsync(int itemId, CancellationToken ct)
    {
        var c = content.Load();
        if (!c.LovePointItems.TryGetValue(itemId, out var amount) || amount <= 0 || c.MaximumLovePoints <= 0) return null;
        var use = await judgement.RuleForAsync(itemId, ct).ConfigureAwait(false);
        return use is { IsReadable: true, IsConsumedOnUse: true, Action: "[love point coupon]" } ? new(use, amount, c.MaximumLovePoints) : null;
    }
}

/// <summary>A coupon whose effects are applied; the points themselves are still to be written by the ledger.</summary>
internal sealed record LovePointSpend(int Amount, int Maximum, BoundInventorySlot Bound, DateTimeOffset At);

/// <summary>Love point coupons: which item counts, the point ceiling, and consuming the coupon.</summary>
internal sealed class LovePoints(IMarriageLovePointRuleService rules, IBusinessInventorySlotDomainService inventory,
    IItemUseEffectRuleService useRules, ICharacterDomainService characters, IApplyEffectsDomainService effects, TimeProvider clock)
{
    public static bool Carries(BusinessSlot slot) => slot.Space.IsCarriedItemSpace();

    /// <param name="points">The relation's points before this coupon.</param>
    /// <returns>0 with the spend once the coupon's effects are applied; otherwise the 03F5 status and no spend.</returns>
    public async ValueTask<(int Status, LovePointSpend? Spend)> SpendAsync(SelectedSessionIdentity actor, BusinessSlot slot, int points,
        CancellationToken ct)
    {
        var bound = inventory.Find(actor.CharacterId, slot);
        var item = bound is null ? null : await bound.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false);
        if (item is null) return (-1, null);
        if (await rules.RuleForAsync(item.ItemId, ct).ConfigureAwait(false) is not { } rule) return (-1, null);
        if (points > rule.Maximum - rule.Amount) return (-2, null);
        var facts = await characters.Get(actor.CharacterId).ItemUseFactsAsync(useRules.CooldownKeyFor(item.ItemId, rule.Use), ct).ConfigureAwait(false);
        var at = clock.GetUtcNow();
        var plan = useRules.Build(rule.Use, slot, item, facts, null, at);
        if (plan.Status != ItemUsability.Allowed) return (-1, null);
        await effects.ApplyAsync(new(actor.AccountId, actor.CharacterId, at, actor), plan.Effects, ct).ConfigureAwait(false);
        return (0, new(rule.Amount, rule.Maximum, bound!, at));
    }

    public async ValueTask<SharedEffectSlotState> RemainingAsync(LovePointSpend spend, CancellationToken ct)
    {
        var remaining = await spend.Bound.Container.GetAsync(spend.Bound.Slot.Index, ct).ConfigureAwait(false);
        return new(remaining?.Snapshot, spend.At);
    }
}
