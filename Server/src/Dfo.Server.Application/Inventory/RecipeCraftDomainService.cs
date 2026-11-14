using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.ServerBroadcast;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class RecipeCraftRuleService(IRecipeContentSource content, IEquipmentDefinitionSource equipment) : IRecipeCraftRuleService
{
    public async ValueTask<RecipePlan> GetResultAsync(int drawingId, CancellationToken ct) =>
        await content.GetAsync(drawingId, ct).ConfigureAwait(false)
        ?? throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unsupported drawing.");

    /// <remarks>
    /// The only probabilistic shape with evidence is the requiem drawing: exactly one equipment input, exactly one
    /// equipment product, <c>[maintain ability] normal</c>, and a fail output that hands that same input back.
    /// The drawing text promises the product is sealed and keeps reinforcement, amplification, forging and enchantment.
    /// </remarks>
    public async ValueTask<RecipeEquipmentUpgrade> EquipmentUpgradeAsync(RecipePlan plan, CancellationToken ct)
    {
        if (plan.Chance is not { MaintainAbility: true } chance) throw Unsupported("Not an equipment upgrade recipe.");
        var pieces = new List<RecipeAmount>();
        foreach (var input in plan.Required)
            if (await equipment.GetEquipmentAsync(input.ItemId, ct).ConfigureAwait(false) is not null) pieces.Add(input);
        if (pieces is not [{ Count: 1 } source] || plan.Produced is not [{ Count: 1 } product])
            throw Unsupported("An upgrade recipe needs one equipment input and one product.");
        var from = await equipment.GetEquipmentAsync(source.ItemId, ct).ConfigureAwait(false);
        var to = await equipment.GetEquipmentAsync(product.ItemId, ct).ConfigureAwait(false);
        var returned = chance.FailOutputs.Where(x => x.ItemId == source.ItemId).ToArray();
        if (from is null || to is null || returned is not [{ Count: 1 }])
            throw Unsupported("An upgrade recipe must hand its equipment back on failure.");
        return new(source.ItemId, product.ItemId, to.AttachType == "sealing", to.MaxDurability, from.MaxDurability,
            chance.SuccessPerTenThousand, chance.FailOutputs.Where(x => x.ItemId != source.ItemId).ToImmutableArray());
    }

    /// <remarks>A21 refuses both locally before sending (compound pre-check); the server codes are 0x13 and 0xD9.</remarks>
    public OpeningRejection AdmitUpgradeInput(RecipeEquipmentUpgrade upgrade, ItemSnapshot piece) =>
        EquipmentAmplification.IsUnpurified(piece) ? OpeningRejection.EquipmentUnpurified
        : upgrade.SourceMaxDurability is { } full && piece.Durability < full ? OpeningRejection.EquipmentUnrepaired
        : OpeningRejection.None;

    private static OpeningRejectedException Unsupported(string detail) => new(OpeningRejection.UnsupportedContent, detail);
}

internal sealed class RecipeChanceRollSource : IRecipeChanceRollSource
{
    public int Next() => Random.Shared.Next(10000);
}

internal sealed class RecipeCraftDomainService(IOpeningInventoryDomainService inventory,
    IRecipeCraftRuleService rules, IPackageOpeningRuleService rewards,
    ICharacterBusinessInventoryDomainService bags, IBusinessInventorySlotDomainService slots, IOpeningEffectPlanRuleService plans,
    IEquipmentDomainService equipments, IRecipeChanceRollSource rolls, IEventService events) : IRecipeCraftDomainService
{
    public async ValueTask<RecipeCraftReceipt> RefreshAsync(RecipeCraftReceipt receipt, CancellationToken ct)
    {
        var session = receipt.Inventory.Session;
        var updated = ImmutableArray.CreateBuilder<OpeningInventoryUpdate>();
        var cleared = ImmutableArray.CreateBuilder<BusinessSlot>();
        foreach (var slot in receipt.Inventory.Updates.Select(x => x.Slot).Concat(receipt.Inventory.Cleared).Distinct())
        {
            var item = await slots.Get(session.CharacterId, slot).Container.GetAsync(slot.Index, ct).ConfigureAwait(false);
            if (item is null) cleared.Add(slot); else updated.Add(new(slot, item.ItemUid, item.Snapshot));
        }
        return receipt with { Inventory = receipt.Inventory with { Updates = updated.ToImmutable(), Cleared = cleared.ToImmutable() } };
    }

    public async ValueTask<RecipeCraftReceipt> CraftAsync(SelectedSessionIdentity session, BusinessSlot drawing,
        int quantity, DateTimeOffset now, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        if (quantity is < 1 or > 1000 || !drawing.Space.IsCarriedItemSpace())
            throw new OpeningRejectedException(OpeningRejection.InvalidRequest, "Invalid crafting quantity or drawing slot.");
        var source = await inventory.SourceAsync(session, drawing, now, ct).ConfigureAwait(false);
        var useRule = await plans.SourceRuleAsync(source.ItemId, ct).ConfigureAwait(false);
        if (useRule.IsConsumedOnUse && source.Count < quantity) throw new OpeningRejectedException(OpeningRejection.MissingMaterial, "Insufficient drawing count.");
        var plan = await rules.GetResultAsync(source.ItemId, ct).ConfigureAwait(false);
        if (plan.Chance is not null)
            return await UpgradeAsync(session, drawing, source, useRule, plan, quantity, now, footprint, ct).ConfigureAwait(false);
        if (plan.Required.IsDefaultOrEmpty || plan.Produced.IsDefaultOrEmpty
            || plan.Required.Length > 64 || plan.Produced.Length > 64
            || plan.Required.Concat(plan.Produced).Any(x => x.ItemId <= 0 || x.Count <= 0)
            || source.Snapshot.EquipmentLockId != 0)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Invalid or locked recipe.");
        // Prefer the selected drawing; otherwise consuming another pile could leave the selected drawing untouched.
        var standing = new List<OpeningHeldItem> { new(drawing, source) };
        foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable,
            BusinessInventorySpace.Material, BusinessInventorySpace.ExpertMaterial, BusinessInventorySpace.SharedMaterial })
        {
            var bag = bags.GetStore(session.CharacterId, BusinessInventoryStoreKind.Item).FindContainer(space);
            if (bag is null) continue;
            standing.AddRange((await bag.GetAllAsync(ct).ConfigureAwait(false))
                .Where(x => new BusinessSlot(space, x.SlotIndex) != drawing)
                .Select(x => new OpeningHeldItem(new BusinessSlot(space, x.SlotIndex), x)));
        }
        var costs = plans.RecipeCosts(plan, source.ItemId, useRule, quantity, standing, now);
        var outputs = plan.Produced.GroupBy(x => x.ItemId)
            .Select(g => new OpeningReward(g.Key, checked((int)(g.Sum(x => (long)x.Count) * quantity)))).ToArray();
        var grants = await rewards.PrepareAsync(outputs, now, ct).ConfigureAwait(false);
        if (grants.Any(g => g.Item.PremiumType != 0 || g.Item.Placement.Destination.Space == BusinessInventorySpace.SharedMaterial
            || g.Item.Placement.WorldClass is ItemWorldClass.Avatar or ItemWorldClass.Creature
            || g.Item.Placement.Destination.Space == BusinessInventorySpace.VirtualResource))
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unsupported crafting output family.");
        var effects = plans.Build(source.ItemId, useRule, costs.Items, grants, [], now)
            .Select(x => x is GrantPreparedItemEffect g ? g with { DeliveryMode = ItemDeliveryMode.InventoryOnly } : x).ToArray();
        var applied = await inventory.ApplyAsync(session, effects, now, footprint, ct).ConfigureAwait(false);
        var receipt = applied.Inventory;
        if (costs.Items.Length > byte.MaxValue || receipt.Rewards.Length > byte.MaxValue)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Crafting receipt exceeds the wire limit.");
        return new(receipt, costs.Items);
    }

    /// <summary>
    /// One attempt of an equipment-upgrade drawing. The drawing and the other inputs are spent either way; on success the
    /// piece changes template in place, on failure it stays as it was and the remaining fail outputs are granted.
    /// </summary>
    private async ValueTask<RecipeCraftReceipt> UpgradeAsync(SelectedSessionIdentity session, BusinessSlot drawing,
        ItemReference source, ItemUseRule useRule, RecipePlan plan, int quantity, DateTimeOffset now,
        IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        // A21 fixes the craftable count at 1 when the product is equipment.
        if (quantity != 1) throw new OpeningRejectedException(OpeningRejection.InvalidRequest, "Upgrade drawings craft one piece at a time.");
        if (source.Snapshot.EquipmentLockId != 0)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Locked drawing.");
        var upgrade = await rules.EquipmentUpgradeAsync(plan, ct).ConfigureAwait(false);
        var bag = bags.GetStore(session.CharacterId, BusinessInventoryStoreKind.Item).FindContainer(BusinessInventorySpace.CarriedEquipment)
            ?? throw new OpeningRejectedException(OpeningRejection.MissingMaterial, "No equipment bag.");
        // The client refuses locally when the bag holds more than one candidate; the server does not guess either.
        var candidates = (await bag.GetAllAsync(ct).ConfigureAwait(false)).Where(x => x.ItemId == upgrade.SourceItemId && !x.IsPile).ToArray();
        if (candidates.Length == 0) throw new OpeningRejectedException(OpeningRejection.MissingMaterial, "The equipment input is not in the equipment bag.");
        if (candidates.Length > 1) throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "Several pieces match the equipment input.");
        var pieceSlot = new BusinessSlot(BusinessInventorySpace.CarriedEquipment, candidates[0].SlotIndex);
        var instance = await equipments.Of(session.CharacterId).FindAtAsync(pieceSlot, ct).ConfigureAwait(false)
            ?? throw new OpeningRejectedException(OpeningRejection.MissingMaterial, "The equipment input is gone.");
        var piece = await instance.ReadAsync(ct).ConfigureAwait(false);
        if (piece.Snapshot.IsMirror || piece.Snapshot.EquipmentLockId != 0 || piece.Snapshot.SortLockFlag != 0
            || piece.Snapshot.Lifetime.IsExpired(now))
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "The equipment input is locked or expired.");
        var admission = rules.AdmitUpgradeInput(upgrade, piece.Snapshot);
        if (admission != OpeningRejection.None)
            throw new OpeningRejectedException(admission, "The equipment input is not admitted.");

        var standing = new List<OpeningHeldItem> { new(drawing, source) };
        foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable,
            BusinessInventorySpace.Material, BusinessInventorySpace.Quest, BusinessInventorySpace.ExpertMaterial,
            BusinessInventorySpace.SharedMaterial })
        {
            var container = bags.GetStore(session.CharacterId, BusinessInventoryOwnership.StoreOf(space)).FindContainer(space);
            if (container is null) continue;
            standing.AddRange((await container.GetAllAsync(ct).ConfigureAwait(false))
                .Where(x => new BusinessSlot(space, x.SlotIndex) != drawing)
                .Select(x => new OpeningHeldItem(new BusinessSlot(space, x.SlotIndex), x)));
        }
        var others = plan with { Required = plan.Required.Where(x => x.ItemId != upgrade.SourceItemId).ToImmutableArray() };
        var costs = plans.RecipeCosts(others, source.ItemId, useRule, 1, standing, now);
        var succeeded = rolls.Next() < upgrade.SuccessPerTenThousand;
        var grants = succeeded || upgrade.FailGrants.IsDefaultOrEmpty ? ImmutableArray<OpeningPreparedGrant>.Empty
            : await rewards.PrepareAsync(upgrade.FailGrants.Select(x => new OpeningReward(x.ItemId, x.Count)).ToArray(), now, ct).ConfigureAwait(false);
        var effects = plans.Build(source.ItemId, useRule, costs.Items, grants, [], now)
            .Select(x => x is GrantPreparedItemEffect g ? g with { DeliveryMode = ItemDeliveryMode.InventoryOnly } : x).ToArray();
        var applied = (await inventory.ApplyAsync(session, effects, now, footprint, ct).ConfigureAwait(false)).Inventory;
        if (costs.Items.Length > byte.MaxValue || applied.Rewards.Length > byte.MaxValue)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Crafting receipt exceeds the wire limit.");
        if (!succeeded) return new(applied, costs.Items);

        // Reinforcement, amplification, forging and enchantment stay with the piece ([maintain ability] normal);
        // the drawing promises a sealed product, so seal it; the new template sets the durability ceiling.
        var after = (await instance.TransformAsync(upgrade.OutputItemId, transformed => transformed with
        {
            SealFlag = upgrade.OutputSealed ? (byte)1 : transformed.SealFlag,
            Durability = upgrade.OutputMaxDurability ?? transformed.Durability,
        }, ct).ConfigureAwait(false)).Snapshot;
        var display = new OpeningRewardDisplay(pieceSlot, after.ItemId, 1, after.Durability, 0, after.ExpireTime, after);
        if (plan.Broadcast)
            await events.EnqueueAsync(new RecipeCraftAnnounced(session.CharacterId, after.ItemId, after.ReinforcementLevel), ct)
                .ConfigureAwait(false);
        return new(applied with
        {
            Updates = applied.Updates.Add(new(pieceSlot, piece.ItemUid, after)),
            Rewards = applied.Rewards.Add(display),
            Replaced = applied.Replaced.Add(pieceSlot),
        }, costs.Items) { Upgraded = pieceSlot };
    }
}
