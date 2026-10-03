using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class OpeningEffectPlanRuleService(IItemUseJudgementService useRules)
    : IOpeningEffectPlanRuleService
{
    public async ValueTask<ItemUseRule> SourceRuleAsync(int itemId, CancellationToken ct) =>
        await useRules.RuleForAsync(itemId, ct).ConfigureAwait(false) is { IsReadable: true } rule ? rule
            : throw Reject(OpeningRejection.ItemUnusable, "Unusable source declaration.");

    public ImmutableArray<OpeningSlotConsumption> SourceCosts(BusinessSlot source, int itemId, ItemUseRule rule, int count)
    {
        if (count <= 0) throw Reject(OpeningRejection.InvalidRequest, "Invalid source quantity.");
        return rule.IsConsumedOnUse ? [new(source, itemId, count)] : [];
    }

    public RecipeEffectCosts RecipeCosts(RecipePlan recipe, int sourceId, ItemUseRule rule, int quantity,
        IReadOnlyList<OpeningHeldItem> held, DateTimeOffset at)
    {
        var required = recipe.Required.AsEnumerable();
        if (rule.IsConsumedOnUse) required = required.Prepend(new RecipeAmount(sourceId, 1));
        var requirements = required.GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => checked(g.Sum(x => (long)x.Count) * quantity));
        if (requirements.Values.Any(x => x > int.MaxValue)) throw Reject(OpeningRejection.InvalidRequest, "Crafting cost overflow.");
        var consumed = ImmutableArray.CreateBuilder<OpeningSlotConsumption>();
        foreach (var (id, amount) in requirements)
        {
            var remaining = checked((int)amount);
            foreach (var standing in held.Where(x => x.Item.ItemId == id && x.Item.IsPile && x.Item.Core.EquipmentLockId == 0
                && (x.Item.Core.ExpireTime is 0 or int.MaxValue || x.Item.Core.ExpireTime > at.ToUnixTimeSeconds())))
            {
                var take = Math.Min(remaining, standing.Item.Count);
                if (take <= 0) continue;
                consumed.Add(new(standing.Slot, id, take)); remaining -= take;
                if (remaining == 0) break;
            }
            if (remaining != 0) throw Reject(OpeningRejection.MissingMaterial, $"Insufficient carried material {id}.");
        }
        return new(consumed.ToImmutable());
    }

    public DoublePotUsageDecision DoublePot(DoublePotUsageFacts facts)
    {
        if (!facts.Eligible || facts.RoundUsed >= facts.RoundLimit || facts.TotalUsed >= facts.TotalLimit)
            throw Reject(OpeningRejection.PremiumUnavailable, "Double pot subscription or daily quota unavailable.");
        return new(facts.Counters, checked(facts.RoundUsed + 1), checked(facts.TotalUsed + 1), facts.At);
    }

    public ImmutableArray<DomainEffect> Build(int sourceId, ItemUseRule rule, IReadOnlyList<OpeningSlotConsumption> consumed,
        ImmutableArray<OpeningPreparedGrant> grants, IReadOnlyList<AccountPremiumEntryState> premiums, DateTimeOffset at,
        int gold = 0, int? luckBefore = null,
        int? luckAfter = null, DoublePotUsageDecision? quota = null)
    {
        var result = ImmutableArray.CreateBuilder<DomainEffect>();
        foreach (var cost in consumed) result.Add(new ConsumeFromContainerEffect(cost.Slot, cost.ItemId, cost.Count));
        if (gold > 0) result.Add(new SpendGoldEffect(gold));
        if (gold < 0) throw Reject(OpeningRejection.UnsupportedContent, "Negative opening gold cost.");
        if (quota is not null) result.Add(new SetDoublePotUsageEffect(quota));
        var expiries = premiums.ToDictionary(x => x.PremiumType, x => x.ExpiresAtUnixSeconds);
        foreach (var grant in grants)
        {
            if (grant.Item.PremiumType > 0)
            {
                var type = grant.Item.PremiumType;
                var old = expiries.GetValueOrDefault(type);
                var duration = checked(grant.Item.PremiumDuration.Ticks * grant.Reward.Count);
                if (duration <= 0 || duration % TimeSpan.TicksPerSecond != 0)
                    throw Reject(OpeningRejection.UnsupportedContent, "Invalid premium duration.");
                var next = checked(Math.Max(at.ToUnixTimeSeconds(), old) + duration / TimeSpan.TicksPerSecond);
                result.Add(new SetOpeningPremiumEffect((PremiumServiceType)type, old, next, grant.Reward));
                expiries[type] = next;
            }
            else if (grant.Item.Placement.Destination.Space == BusinessInventorySpace.VirtualResource)
            {
                var range = grant.Item.Placement.Destination;
                if (range.FirstIndex != range.LastIndex || range.FirstIndex is < 0 or > 2)
                    throw Reject(OpeningRejection.UnsupportedContent, "Invalid virtual reward destination.");
                result.Add(new GrantOpeningVirtualEffect(range.FirstIndex, grant.Item.ItemId, grant.Item.Placement.ItemKind, grant.Reward));
            }
            else result.Add(new GrantPreparedItemEffect(grant));
        }
        if (luckBefore is { } before && luckAfter is { } after) result.Add(new SetSeriaLuckEffect(before, after));
        else if (luckBefore is not null || luckAfter is not null) throw Reject(OpeningRejection.InvalidRequest, "Incomplete luck decision.");
        if (rule.CooldownMilliseconds > 0)
            result.Add(new StartItemCooldownEffect(useRules.CooldownKeyFor(sourceId, rule), TimeSpan.FromMilliseconds(rule.CooldownMilliseconds)));
        return result.ToImmutable();
    }
    private static OpeningRejectedException Reject(OpeningRejection reason, string detail) => new(reason, detail);
}
