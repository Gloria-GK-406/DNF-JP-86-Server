using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class PackageOpeningRuleService(IPackageOpeningDefinitionSource content) : IPackageOpeningRuleService
{
    public async ValueTask<OpeningPackageDefinition> DefinitionAsync(int itemId, OpeningPackageKind kind, CancellationToken ct)
    {
        var definition = await content.GetAsync(itemId, ct).ConfigureAwait(false);
        if (definition is null || definition.Kind != kind
            && !(kind is OpeningPackageKind.Package or OpeningPackageKind.AvatarPackage && definition.Kind == OpeningPackageKind.Selection))
            throw new OpeningRejectedException(OpeningRejection.WrongItem, $"Item {itemId} does not belong to {kind}.");
        if (!definition.Diagnostics.IsEmpty)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, string.Join("; ", definition.Diagnostics));
        return definition;
    }

    public ImmutableArray<OpeningReward> Roll(OpeningPackageDefinition definition, IReadOnlyList<int> choices, int seed)
    {
        var rewards = ImmutableArray.CreateBuilder<OpeningReward>();
        if (definition.Kind == OpeningPackageKind.Selection && definition.SelectionCount > 0)
        {
            if (choices.Count != definition.SelectionCount || choices.Distinct().Count() != choices.Count)
                throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "Selection count or uniqueness disagrees.");
            foreach (var id in choices)
                rewards.Add(definition.FixedRewards.SingleOrDefault(r => r.ItemId == id)
                    ?? throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "Undeclared selection."));
        }
        else
        {
            if (choices.Count != 0) throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "Unexpected selection.");
            rewards.AddRange(definition.FixedRewards);
        }
        var random = new Random(seed);
        foreach (var pool in definition.Pools)
        {
            if (pool.Draws is < 1 or > 1024) throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Invalid draw count.");
            var total = pool.Choices.Sum(row => (long)row.Weight);
            if (total <= 0) throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Empty pool.");
            for (var draw = 0; draw < pool.Draws; draw++)
            {
                var ticket = random.NextInt64(total);
                foreach (var row in pool.Choices)
                {
                    ticket -= row.Weight;
                    if (ticket >= 0) continue;
                    rewards.Add(row.Reward);
                    break;
                }
            }
        }
        return rewards.ToImmutable();
    }

    public async ValueTask<ImmutableArray<OpeningPreparedGrant>> PrepareAsync(IReadOnlyList<OpeningReward> rewards, DateTimeOffset now, CancellationToken ct)
    {
        if (rewards.Count is 0 or > 8192) throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Invalid reward count.");
        var result = ImmutableArray.CreateBuilder<OpeningPreparedGrant>();
        foreach (var reward in rewards)
        {
            var item = await content.GetRewardAsync(reward.ItemId, ct).ConfigureAwait(false);
            if (item is not { Supported: true } || reward.Count <= 0 || reward.PeriodDays < 0)
                throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, $"Unsupported reward {reward.ItemId}.");
            if (item.Placement.WorldClass == ItemWorldClass.Avatar
                && (item.Abilities.IsEmpty ? reward.Ability != 0 : !item.Abilities.Contains(reward.Ability))
                || item.Placement.WorldClass != ItemWorldClass.Avatar && reward.Ability != 0)
                throw new OpeningRejectedException(OpeningRejection.InvalidChoice, $"Undeclared ability for {reward.ItemId}.");
            if (item.Placement.WorldClass == ItemWorldClass.Creature && (item.CreatureStartLevel is null || reward.PeriodDays != 0))
                throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unsupported creature creation.");
            var days = reward.PeriodDays == 0 ? item.DefaultPeriodDays : item.DefaultPeriodDays == 0
                ? reward.PeriodDays : Math.Min(reward.PeriodDays, item.DefaultPeriodDays);
            var expiration = days == 0 ? 0 : checked((int)now.AddDays(days).ToUnixTimeSeconds());
            if (item.AbsoluteExpiration > 0) expiration = expiration == 0 ? item.AbsoluteExpiration : Math.Min(expiration, item.AbsoluteExpiration);
            if (expiration != 0 && expiration <= now.ToUnixTimeSeconds())
                throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, $"Reward {reward.ItemId} has already expired.");
            result.Add(new(reward, item, expiration));
        }
        return result.ToImmutable();
    }

    public bool LuckBonus(int value) => value == 8;
    public int AdvanceLuck(int value) => value is < 0 or > 8
        ? throw new OpeningRejectedException(OpeningRejection.StateChanged, "Invalid Seria luck.") : value == 8 ? 1 : value + 1;
}
