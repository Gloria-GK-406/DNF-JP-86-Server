using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class PackageOpeningDomainService(IOpeningInventoryDomainService inventory,
    IPackageOpeningRuleService rules, IOpeningEffectPlanRuleService plans) : IPackageOpeningDomainService
{
    public async ValueTask<PackageOpeningResult> OpenAsync(SelectedSessionIdentity session, BusinessPackageOpeningInput input, DateTimeOffset now, CancellationToken ct)
    {
        if (input.SelectedItems.IsDefault) throw new OpeningRejectedException(OpeningRejection.InvalidRequest, "Missing selections.");
        var sourceSlot = input.SourceSlot;
        var source = await inventory.SourceAsync(session, sourceSlot, now, ct).ConfigureAwait(false);
        var useRule = await plans.SourceRuleAsync(source.ItemId, ct).ConfigureAwait(false);
        var definition = await rules.DefinitionAsync(source.ItemId, OpeningPackageKind.Package, ct).ConfigureAwait(false);
        if (definition.MaterialCount != 0) throw new OpeningRejectedException(OpeningRejection.MissingMaterial, "This entry supplies no material.");
        var rolled = rules.Roll(definition, input.SelectedItems, Random.Shared.Next());
        var grants = await rules.PrepareAsync(rolled, now, ct).ConfigureAwait(false);
        var plan = plans.Build(source.ItemId, useRule, plans.SourceCosts(sourceSlot, source.ItemId, useRule, definition.SourceCount),
            grants, await inventory.ReadPremiumsAsync(session, ct).ConfigureAwait(false), now);
        return new(OpeningRejection.None, (await inventory.ApplyAsync(session, plan, now, ct).ConfigureAwait(false)).Inventory);
    }
}

internal sealed class AvatarPackageOpeningDomainService(IOpeningInventoryDomainService inventory,
    IPackageOpeningRuleService rules, IOpeningEffectPlanRuleService plans) : IAvatarPackageOpeningDomainService
{
    public async ValueTask<PackageOpeningResult> OpenAsync(SelectedSessionIdentity session, BusinessAvatarPackageOpeningInput input, DateTimeOffset now, CancellationToken ct)
    {
        if (input.Choices.IsDefault || input.Choices.Length > 32 || input.Choices.Select(c => c.ItemId).Distinct().Count() != input.Choices.Length)
            throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "Missing or duplicated avatar choices.");
        var sourceSlot = input.SourceSlot;
        var source = await inventory.SourceAsync(session, sourceSlot, now, ct).ConfigureAwait(false);
        var useRule = await plans.SourceRuleAsync(source.ItemId, ct).ConfigureAwait(false);
        var definition = await rules.DefinitionAsync(source.ItemId, OpeningPackageKind.AvatarPackage, ct).ConfigureAwait(false);
        if (definition.MaterialCount != 0 || !definition.Pools.IsEmpty)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Avatar package has material or random declarations.");
        var choices = input.Choices.ToDictionary(c => c.ItemId, c => c.Ability);
        var selected = definition.Kind == OpeningPackageKind.Selection
            ? rules.Roll(definition, input.Choices.Select(c => c.ItemId).ToArray(), Random.Shared.Next()) : definition.FixedRewards;
        var rewards = selected.Select(r => choices.TryGetValue(r.ItemId, out var ability) ? r with { Ability = ability } : r).ToArray();
        var grants = await rules.PrepareAsync(rewards, now, ct).ConfigureAwait(false);
        var avatarIds = grants.Where(g => g.Item.Placement.WorldClass == ItemWorldClass.Avatar).Select(g => g.Reward.ItemId).ToHashSet();
        if (!avatarIds.SetEquals(choices.Keys))
            throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "Avatar choices must match all granted avatars.");
        var plan = plans.Build(source.ItemId, useRule, plans.SourceCosts(sourceSlot, source.ItemId, useRule, definition.SourceCount),
            grants, await inventory.ReadPremiumsAsync(session, ct).ConfigureAwait(false), now);
        return new(OpeningRejection.None, (await inventory.ApplyAsync(session, plan, now, ct).ConfigureAwait(false)).Inventory);
    }
}

internal sealed class MagicBoxOpeningDomainService(IOpeningInventoryDomainService inventory,
    IPackageOpeningRuleService rules, IAccountTable accounts, IOpeningEffectPlanRuleService plans) : IMagicBoxOpeningDomainService
{
    public async ValueTask<MagicBoxOpeningResult> OpenAsync(SelectedSessionIdentity session, BusinessMagicBoxOpeningInput input, DateTimeOffset now, CancellationToken ct)
    {
        if (input.ListType is not (0 or 4 or 5) || input.Count is < 1 or > 1000)
            throw new OpeningRejectedException(OpeningRejection.InvalidRequest, "Unsupported box list or count.");
        var sourceSlot = input.SourceSlot;
        var source = await inventory.SourceAsync(session, sourceSlot, now, ct).ConfigureAwait(false);
        if (input.ExpectedItemId > 0 && source.ItemId != input.ExpectedItemId)
            throw new OpeningRejectedException(OpeningRejection.WrongItem, "Box identifier disagrees.");
        var useRule = await plans.SourceRuleAsync(source.ItemId, ct).ConfigureAwait(false);
        var definition = await rules.DefinitionAsync(source.ItemId, OpeningPackageKind.MagicBox, ct).ConfigureAwait(false);
        if (definition.SourceCount != 1 || input.ExpectedMaterialId > 0 && input.ExpectedMaterialId != definition.MaterialItemId
            || definition.MaterialCount > 0 && (input.MaterialSlot is null || input.MaterialSlot == input.SourceSlot)
            || definition.MaterialCount == 0 && input.MaterialSlot is not null)
            throw new OpeningRejectedException(OpeningRejection.InvalidRequest, "Unsupported source count or mismatched material.");
        if (useRule.IsConsumedOnUse && source.Count < input.Count) throw new OpeningRejectedException(OpeningRejection.WrongItem, "Insufficient box count.");
        var consumed = plans.SourceCosts(sourceSlot, source.ItemId, useRule, input.Count).ToList();
        if (definition.MaterialCount > 0) consumed.Add(new(input.MaterialSlot!.Value, definition.MaterialItemId, checked(input.Count * definition.MaterialCount)));
        var before = definition.UsesSeriaLuck ? await accounts.FindSeriaLuckAsync(session.AccountId, ct).ConfigureAwait(false) : null;
        if (definition.UsesSeriaLuck && before is null) throw new OpeningRejectedException(OpeningRejection.NoSession, "Account luck is absent.");
        var luck = before ?? 0;
        var primary = ImmutableArray.CreateBuilder<OpeningReward>();
        var bonus = ImmutableArray.CreateBuilder<OpeningReward>();
        for (var i = 0; i < input.Count; i++)
        {
            var rolled = rules.Roll(definition, [], Random.Shared.Next());
            primary.AddRange(rolled);
            if (definition.UsesSeriaLuck)
            {
                if (rules.LuckBonus(luck)) bonus.AddRange(rolled);
                luck = rules.AdvanceLuck(luck);
            }
        }
        // Reference behavior awards the base plus two extra copies for a lucky opening.
        var rewards = primary.Concat(bonus.Select(r => r with { Count = checked(r.Count * 2) })).ToArray();
        var grants = await rules.PrepareAsync(rewards, now, ct).ConfigureAwait(false);
        var plan = plans.Build(source.ItemId, useRule, consumed, grants,
            await inventory.ReadPremiumsAsync(session, ct).ConfigureAwait(false), now, luckBefore: before, luckAfter: before is null ? null : luck);
        var receipt = (await inventory.ApplyAsync(session, plan, now, ct).ConfigureAwait(false)).Inventory;
        ImmutableArray<OpeningRewardDisplay> Display(IEnumerable<OpeningReward> entries) => entries.GroupBy(r => r.ItemId).Select(g =>
            receipt.Rewards.First(r => r.ItemId == g.Key) with { Count = checked(g.Sum(r => r.Count)) }).ToImmutableArray();
        return new(OpeningRejection.None, receipt, Display(primary), Display(bonus), before, before is null ? null : luck);
    }
}
