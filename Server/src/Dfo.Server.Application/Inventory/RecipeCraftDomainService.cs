using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class RecipeCraftRuleService(IRecipeContentSource content) : IRecipeCraftRuleService
{
    public async ValueTask<RecipePlan> GetResultAsync(int drawingId, CancellationToken ct) =>
        await content.GetAsync(drawingId, ct).ConfigureAwait(false)
        ?? throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unsupported drawing.");
}

internal sealed class RecipeCraftDomainService(IOpeningInventoryDomainService inventory,
    IRecipeCraftRuleService rules, IPackageOpeningRuleService rewards,
    ICharacterBusinessInventoryDomainService bags, IAccountMaterialRuleService accountRules,
    IAccountMaterialBagDomainService accountBags, ICharacterInventoryEntryDomainService entries, IOpeningEffectPlanRuleService plans) : IRecipeCraftDomainService
{
    public async ValueTask<RecipeCraftReceipt> RefreshAsync(RecipeCraftReceipt receipt, CancellationToken ct)
    {
        var session = receipt.Inventory.Session;
        var snapshot = await entries.ReadCurrentAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var updated = ImmutableArray.CreateBuilder<OpeningInventoryUpdate>();
        var cleared = ImmutableArray.CreateBuilder<BusinessSlot>();
        foreach (var slot in receipt.Inventory.Updates.Select(x => x.Slot).Concat(receipt.Inventory.Cleared).Distinct())
        {
            var storage = LegacyInventoryStorageCoordinates.ToStorage(slot);
            var item = snapshot.Containers.Single(x => x.ContainerType == storage.List).Items.SingleOrDefault(x => x.SlotIndex == storage.Index);
            if (item is null) cleared.Add(slot); else updated.Add(new(slot, item.Identity, item.Core));
        }
        var balances = await accountBags.Get(session.AccountId).SnapshotAsync(ct).ConfigureAwait(false);
        return receipt with { Inventory = receipt.Inventory with { Updates = updated.ToImmutable(), Cleared = cleared.ToImmutable() },
            AccountMaterials = receipt.AccountMaterials.Select(x => x with { FinalCount = balances.Count(x.Kind) }).ToImmutableArray() };
    }

    public async ValueTask<RecipeCraftReceipt> CraftAsync(SelectedSessionIdentity session, BusinessSlot drawing,
        int quantity, DateTimeOffset now, CancellationToken ct)
    {
        if (quantity is < 1 or > 1000 || !drawing.Space.IsCarriedItemSpace())
            throw new OpeningRejectedException(OpeningRejection.InvalidRequest, "Invalid crafting quantity or drawing slot.");
        var source = await inventory.SourceAsync(session, drawing, now, ct).ConfigureAwait(false);
        var useRule = await plans.SourceRuleAsync(source.ItemId, ct).ConfigureAwait(false);
        if (useRule.IsConsumedOnUse && source.Count < quantity) throw new OpeningRejectedException(OpeningRejection.MissingMaterial, "Insufficient drawing count.");
        var plan = await rules.GetResultAsync(source.ItemId, ct).ConfigureAwait(false);
        if (plan.Required.IsDefaultOrEmpty || plan.Produced.IsDefaultOrEmpty
            || plan.Required.Length > 64 || plan.Produced.Length > 64
            || plan.Required.Concat(plan.Produced).Any(x => x.ItemId <= 0 || x.Count <= 0)
            || source.Core.EquipmentLockId != 0)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Invalid or locked recipe.");
        var balance = await accountBags.Get(session.AccountId).SnapshotAsync(ct).ConfigureAwait(false);
        // Prefer the selected drawing; otherwise consuming another pile could leave the selected drawing untouched.
        var standing = new List<OpeningHeldItem> { new(drawing, source) };
        foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable,
            BusinessInventorySpace.Material, BusinessInventorySpace.ExpertMaterial })
        {
            var bag = bags.GetStore(session.CharacterId, BusinessInventoryStoreKind.Item).FindContainer(space);
            if (bag is null) continue;
            standing.AddRange((await bag.ReadAllAsync(ct).ConfigureAwait(false))
                .Where(x => new BusinessSlot(space, x.SlotIndex) != drawing)
                .Select(x => new OpeningHeldItem(new BusinessSlot(space, x.SlotIndex), x)));
        }
        var costs = plans.RecipeCosts(plan, source.ItemId, useRule, quantity, standing, balance, now);
        var outputs = plan.Produced.GroupBy(x => x.ItemId)
            .Select(g => new OpeningReward(g.Key, checked((int)(g.Sum(x => (long)x.Count) * quantity)))).ToArray();
        var grants = await rewards.PrepareAsync(outputs, now, ct).ConfigureAwait(false);
        if (grants.Any(g => g.Item.PremiumType != 0 || accountRules.TryGetByItemId(g.Reward.ItemId, out _)
            || g.Item.Placement.WorldClass is ItemWorldClass.Avatar or ItemWorldClass.Creature
            || g.Item.Placement.Destination.Space == BusinessInventorySpace.VirtualResource))
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unsupported crafting output family.");
        var effects = plans.Build(source.ItemId, useRule, costs.Items, grants, [], now, accountCosts: costs.Accounts);
        var applied = await inventory.ApplyAsync(session, effects, now, ct).ConfigureAwait(false);
        var receipt = applied.Inventory;
        var changed = applied.Applied.Changes<AccountMaterialEffectChange>().Select(x => x.Change).ToImmutableArray();
        if (costs.Items.Length + changed.Length > byte.MaxValue || receipt.Rewards.Length > byte.MaxValue)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Crafting receipt exceeds the wire limit.");
        return new(receipt, costs.Items, changed);
    }
}
