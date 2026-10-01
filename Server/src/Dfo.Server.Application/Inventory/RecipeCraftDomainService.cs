using System.Collections.Immutable;
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
    IAccountMaterialBagDomainService accountBags, ICharacterInventoryEntryDomainService entries) : IRecipeCraftDomainService
{
    public async ValueTask<RecipeCraftReceipt> RefreshAsync(RecipeCraftReceipt receipt, CancellationToken ct)
    {
        var session = receipt.Inventory.Session;
        var snapshot = await entries.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
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
        if (source.Count < quantity) throw new OpeningRejectedException(OpeningRejection.MissingMaterial, "Insufficient drawing count.");
        var plan = await rules.GetResultAsync(source.ItemId, ct).ConfigureAwait(false);
        if (plan.Required.IsDefaultOrEmpty || plan.Produced.IsDefaultOrEmpty
            || plan.Required.Length > 64 || plan.Produced.Length > 64
            || plan.Required.Concat(plan.Produced).Any(x => x.ItemId <= 0 || x.Count <= 0)
            || source.Core.EquipmentLockId != 0)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Invalid or locked recipe.");
        // The drawing is consumed once for each craft. Allocate all physical inputs before changing anything.
        var requirements = plan.Required.Prepend(new(source.ItemId, 1)).GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => checked(g.Sum(x => (long)x.Count) * quantity));
        if (requirements.Values.Any(x => x > int.MaxValue))
            throw new OpeningRejectedException(OpeningRejection.InvalidRequest, "Crafting cost overflow.");
        var consumed = new List<OpeningSlotConsumption>();
        var accountCosts = new List<(AccountMaterialKind Kind, int Count)>();
        var accountBag = accountBags.Get(session.AccountId);
        var balance = await accountBag.SnapshotAsync(ct).ConfigureAwait(false);
        // Prefer the selected drawing; otherwise consuming another pile could leave the selected drawing untouched.
        var standing = new List<(BusinessSlot Slot, ContainerSlotItem Item)> { (drawing, source) };
        foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable,
            BusinessInventorySpace.Material, BusinessInventorySpace.ExpertMaterial })
        {
            var bag = bags.GetStore(session.CharacterId, BusinessInventoryStoreKind.Item).FindContainer(space);
            if (bag is null) continue;
            standing.AddRange((await bag.ReadAllAsync(ct).ConfigureAwait(false))
                .Where(x => new BusinessSlot(space, x.SlotIndex) != drawing)
                .Select(x => (new BusinessSlot(space, x.SlotIndex), x)));
        }
        foreach (var (id, amount) in requirements)
        {
            var remaining = checked((int)amount);
            if (accountRules.TryGetByItemId(id, out var material))
            {
                if (balance.Count(material.Kind) < remaining)
                    throw new OpeningRejectedException(OpeningRejection.MissingMaterial, $"Insufficient account material {id}.");
                accountCosts.Add((material.Kind, remaining));
                continue;
            }
            foreach (var held in standing.Where(x => x.Item.ItemId == id && x.Item.IsPile
                && x.Item.Core.EquipmentLockId == 0
                && (x.Item.Core.ExpireTime == 0 || x.Item.Core.ExpireTime > now.ToUnixTimeSeconds())))
            {
                var take = Math.Min(remaining, held.Item.Count);
                if (take <= 0) continue;
                consumed.Add(new(held.Slot, id, take)); remaining -= take;
                if (remaining == 0) break;
            }
            if (remaining != 0)
                throw new OpeningRejectedException(OpeningRejection.MissingMaterial, $"Insufficient carried material {id}.");
        }
        var outputs = plan.Produced.GroupBy(x => x.ItemId)
            .Select(g => new OpeningReward(g.Key, checked((int)(g.Sum(x => (long)x.Count) * quantity)))).ToArray();
        var grants = await rewards.PrepareAsync(outputs, now, ct).ConfigureAwait(false);
        if (grants.Any(g => g.Item.PremiumType != 0 || accountRules.TryGetByItemId(g.Reward.ItemId, out _)
            || g.Item.Placement.WorldClass is ItemWorldClass.Avatar or ItemWorldClass.Creature
            || g.Item.Placement.Destination.Space == BusinessInventorySpace.VirtualResource))
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unsupported crafting output family.");
        var changed = ImmutableArray.CreateBuilder<AccountMaterialChange>();
        foreach (var cost in accountCosts)
            changed.Add(await accountBag.ConsumeAsync(cost.Kind, cost.Count, ct).ConfigureAwait(false)
                ?? throw new OpeningRejectedException(OpeningRejection.StateChanged, "Account material changed."));
        // Shared inventory settlement plans room after consumption, assigns real item identities and persists.
        // Any exception must escape the command so its transaction rolls back account and item writes together.
        var receipt = await inventory.SettleAsync(session, consumed, grants, now, ct).ConfigureAwait(false);
        if (consumed.Count + changed.Count > byte.MaxValue || receipt.Rewards.Length > byte.MaxValue)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Crafting receipt exceeds the wire limit.");
        return new(receipt, consumed.ToImmutableArray(), changed.ToImmutable());
    }
}
