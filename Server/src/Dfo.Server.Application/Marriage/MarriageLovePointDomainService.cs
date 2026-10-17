using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
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
internal sealed class MarriageLovePointDomainService(IMarriageProposalDomainStore proposals, IMarriageWeddingDomainStore weddings,
    IMarriageLovePointRuleService rules, ISelectedSessionDomainService sessions, IBusinessInventorySlotDomainService inventory,
    IItemUseEffectRuleService useRules, ICharacterDomainService characters, IApplyEffectsDomainService effects, TimeProvider clock) : IMarriageLovePointDomainService
{
    public async ValueTask<IReadOnlyList<AccountId>> AccountsAsync(SelectedSessionIdentity actor, CancellationToken ct)
    {
        if (sessions.Find(actor.AccountId) != actor || await proposals.FindActiveAsync(actor.CharacterId, ct).ConfigureAwait(false) is not { State: MarriageProposalState.Engaged } relation) return [actor.AccountId];
        return new[] { relation.Proposer.AccountId, relation.Recipient.AccountId }.Distinct().OrderBy(x => x.Value).ToArray();
    }
    public async ValueTask<MarriageLovePointResult> UseAsync(SelectedSessionIdentity actor, BusinessSlot slot, IReadOnlyList<AccountId> accounts, CancellationToken ct)
    {
        if (sessions.Find(actor.AccountId) != actor || !slot.Space.IsCarriedItemSpace()
            || !accounts.SequenceEqual(await AccountsAsync(actor, ct).ConfigureAwait(false))) return new(-1);
        var relation = await proposals.FindActiveAsync(actor.CharacterId, ct).ConfigureAwait(false);
        if (relation is not { State: MarriageProposalState.Engaged }) return new(-3);
        var wedding = await weddings.MarriedAsync(relation.Id, ct).ConfigureAwait(false);
        if (wedding is null) return new(-3);
        var bound = inventory.Find(actor.CharacterId, slot);
        var item = bound is null ? null : await bound.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false);
        if (item is null) return new(-1);
        if (await rules.RuleForAsync(item.ItemId, ct).ConfigureAwait(false) is not { } rule) return new(-1);
        if (wedding.LovePoints > rule.Maximum - rule.Amount) return new(-2);
        var facts = await characters.Get(actor.CharacterId).ItemUseFactsAsync(useRules.CooldownKeyFor(item.ItemId, rule.Use), ct).ConfigureAwait(false);
        var at = clock.GetUtcNow();
        var plan = useRules.Build(rule.Use, slot, item, facts, null, at);
        if (plan.Status != ItemUsability.Allowed) return new(-1);
        await effects.ApplyAsync(new(actor.AccountId, actor.CharacterId, at, actor), plan.Effects, ct).ConfigureAwait(false);
        if (!await weddings.AddLovePointsAsync(relation.Id, rule.Amount, rule.Maximum, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Love point settlement changed under the protected account transaction.");
        var remaining = await bound!.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false);
        return new(0, wedding.LovePoints + rule.Amount,
            relation.Proposer.CharacterId == actor.CharacterId ? relation.Recipient.CharacterId : relation.Proposer.CharacterId)
        { State = new(remaining?.Snapshot, at) };
    }
}
