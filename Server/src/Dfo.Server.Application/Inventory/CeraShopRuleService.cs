using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class CeraShopRuleService(ICeraShopDefinitionSource content,
    IItemPlacementRuleService placements, IItemArrivalRuleService arrivals,
    IInventorySlotMappingRuleService? slotMappings = null) : ICeraShopRuleService
{
    private readonly IInventorySlotMappingRuleService slotMappings =
        slotMappings ?? new InventorySlotMappingRuleService();
    public async ValueTask<CeraShopQuoteResult> QuoteAsync(CeraShopPurchaseInput input, int characterLevel,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (input.InvalidReason is not null || input.Header0 != 0 || input.Header1 != 0 || input.CouponIndicator != 0
            || input.Entries.IsDefaultOrEmpty || input.Entries.Length > byte.MaxValue)
            return new(CeraShopRejection.InvalidRequest, null, input.InvalidReason ?? "Unsupported purchase header or cart size.");
        var products = ImmutableArray.CreateBuilder<int>();
        var charges = ImmutableArray.CreateBuilder<CeraShopCharge>();
        var grants = ImmutableArray.CreateBuilder<CeraShopGrant>();
        var subscriptions = ImmutableArray.CreateBuilder<CeraShopSubscriptionGrant>();
        try
        {
            foreach (var entry in input.Entries)
            {
                if (entry is null || entry.Mode != 0 || entry.CouponItemId != 0 || entry.CouponSlot != 0
                    || entry.AvatarChoices.IsDefault || entry.Selections.IsDefault)
                    throw Refuse(CeraShopRejection.InvalidRequest, "Unsupported purchase entry or coupon.");
                var product = await content.GetProductAsync(entry.ProductId, cancellationToken).ConfigureAwait(false)
                    ?? throw Refuse(CeraShopRejection.UnknownProduct, $"Unknown product {entry.ProductId}.");
                if (!product.Diagnostics.IsEmpty || product.Price is not { } price)
                    throw Refuse(CeraShopRejection.UnsupportedProduct, $"Product {entry.ProductId}: service or {Diagnostics(product.Diagnostics)}.");
                if (product.ItemId <= 0 || product.Count <= 0 || price.Cera < 0 || price.Gold < 0 || price.OtherPayment != 0)
                    throw Refuse(CeraShopRejection.UnsupportedProduct, $"Product {entry.ProductId}: invalid quantity or price.");
                if (product.IsService)
                {
                    if (product.Category != "[selectable character premium]" || product.ServiceSlot is not (>= 0 and < 8)
                        || product.ServiceDays is not (7 or 30) || product.Count != 1)
                        throw Refuse(CeraShopRejection.UnsupportedProduct, "Unsupported subscription declaration.");
                    if (!entry.AvatarChoices.IsEmpty || !entry.Selections.IsEmpty
                        || entry.Attribute is not (0 or 255) || entry.Variant is not (0 or 255))
                        throw Refuse(CeraShopRejection.InvalidChoice, "Unexpected subscription options.");
                    var service = product.ServiceSlot.Value switch
                    {
                        0 => PremiumServiceType.FreeGoldCard, 1 => PremiumServiceType.DoublePot,
                        2 => PremiumServiceType.QuestHelper, 3 => PremiumServiceType.DungeonBuff,
                        4 => PremiumServiceType.AutoHpMpRecovery, 5 => PremiumServiceType.FreeWeaknessRecovery,
                        6 => PremiumServiceType.AutoRepair, 7 => PremiumServiceType.FastPot,
                        _ => throw new InvalidOperationException(),
                    };
                    subscriptions.Add(new(service, TimeSpan.FromDays(product.ServiceDays)));
                    products.Add(product.ProductId);
                    charges.Add(new(product.ProductId, price.Gold, price.Cera,
                        product.BuyOnlyCera ? CeraShopPaymentMode.CeraOnly : product.BuyOnlyCeraPoint
                            ? CeraShopPaymentMode.PointsOnly : CeraShopPaymentMode.Ordinary));
                    continue;
                }
                var choices = new Choices(entry);
                var root = await content.GetItemAsync(product.ItemId, cancellationToken).ConfigureAwait(false)
                    ?? throw Refuse(CeraShopRejection.UnsupportedProduct, $"Missing item {product.ItemId}.");
                if (root.Kind != CeraShopItemKind.Package && (!entry.AvatarChoices.IsEmpty || !entry.Selections.IsEmpty))
                    throw Refuse(CeraShopRejection.InvalidChoice, "Options lists require a package declaration.");
                await ExpandAsync(root, product.Count, Expiration(price.Period, now), entry.Attribute,
                    root.Kind == CeraShopItemKind.Package, choices, [], grants, characterLevel, now, cancellationToken)
                    .ConfigureAwait(false);
                choices.RequireAllUsed();
                products.Add(product.ProductId);
                charges.Add(new(product.ProductId, price.Gold, price.Cera,
                    product.BuyOnlyCera ? CeraShopPaymentMode.CeraOnly : product.BuyOnlyCeraPoint
                        ? CeraShopPaymentMode.PointsOnly : CeraShopPaymentMode.Ordinary));
            }
            return new(CeraShopRejection.None, new(products.ToImmutable(), charges.ToImmutable(), grants.ToImmutable()) { Subscriptions = subscriptions.ToImmutable() }, null);
        }
        catch (QuoteRefusal failure) { return new(failure.Reason, null, failure.Message); }
        catch (OverflowException) { return new(CeraShopRejection.UnsupportedProduct, null, "Purchase quantity or expiration cannot be represented."); }
    }

    public CeraShopPlanningResult Plan(CeraShopQuote quote,
        IReadOnlyDictionary<InventoryContainerType, IReadOnlyList<ContainerSlotItem>> held,
        int gold, AccountCeraBalances balances, int identitySeed)
    {
        if (balances.Cera < 0 || balances.TokenCera < 0 || balances.HappyTokenCera < 0 || gold < 0)
            return new(CeraShopRejection.StateChanged, null, "Invalid current balances.");
        long goldCost = 0;
        var after = balances;
        foreach (var charge in quote.Charges)
        {
            if (charge.Gold < 0 || charge.Cera < 0)
                return new(CeraShopRejection.UnsupportedProduct, null, "Negative quoted cost.");
            goldCost += charge.Gold;
            if (goldCost > gold || !TryPay(after, charge.Cera, charge.Mode, out after))
                return new(CeraShopRejection.InsufficientFunds, null, $"Insufficient funds for product {charge.ProductId}.");
        }

        var standing = held.ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(item => item.SlotIndex));
        var writes = new Dictionary<(InventoryContainerType, int), CeraShopSlotWrite>();
        var serial = 0L;
        foreach (var grant in quote.Grants)
        {
            var placement = grant.Placement;
            var container = this.ContainerFor(placement.Destination);
            if (!standing.TryGetValue(container, out var slots))
                return new(CeraShopRejection.NoSession, null, $"Container {container} is not bound.");
            var baseline = StackableItemCore.Create(placement.ItemKind, grant.Item.ItemId, 0) with
            {
                Attribute = grant.Ability,
                Durability = grant.Item.Durability,
                SealFlag = grant.Item.Kind == CeraShopItemKind.Creature ? (byte)0 : grant.Item.Sealed ? (byte)1 : (byte)0,
                ExpireTime = grant.Item.Kind == CeraShopItemKind.Avatar ? 0 : grant.ExpireDate,
            };
            var outstanding = grant.Count;
            while (outstanding > 0)
            {
                var candidates = CandidateSlots(placement).ToArray();
                var businessPiles = candidates
                    .Select(slot => (Slot: slot, Index: this.StorageIndex(container, slot)))
                    .Where(candidate => slots.TryGetValue(candidate.Index, out var item)
                        && item.IsPile
                        && item.ItemId == grant.Item.ItemId
                        && item.Core with { Value = 0 } == baseline)
                    .Select(candidate => new ItemPile(
                        candidate.Slot, slots[candidate.Index].Count)).ToArray();
                var arrival = arrivals.ForGrant(businessPiles, outstanding, placement,
                    candidates.Where(slot => slots.ContainsKey(this.StorageIndex(container, slot)))
                        .ToHashSet());
                int slot, added;
                InventoryItemCoreState core;
                InventoryItemCreationFacts? facts = null;
                if (arrival is ItemArrival.OntoExistingPile pile)
                {
                    slot = this.StorageIndex(container, pile.Slot);
                    added = pile.Added;
                    core = slots[slot].Core with { Value = pile.Total };
                }
                else if (arrival is ItemArrival.IntoEmptySlot empty)
                {
                    slot = this.StorageIndex(container, empty.Slot);
                    added = empty.Count;
                    var value = placement.WorldClass == ItemWorldClass.Interchangeable ? added
                        : (int)(((long)(uint)identitySeed + serial++) % (int.MaxValue - 1)) + 1;
                    core = baseline with { Value = value };
                    facts = grant.Item.Kind switch
                    {
                        CeraShopItemKind.Avatar => new(new(grant.ExpireDate,
                            0, new byte[30], 0, 0, 0)),
                        // Initial satiety and flags follow the reference creation policy; level is PVF data.
                        CeraShopItemKind.Creature => new(Creature: new(100, 0, 0, 0, 0,
                            grant.Item.Creature!.StartLevel, [], 0)),
                        _ => null,
                    };
                }
                else return new(CeraShopRejection.NoRoom, null, $"No room in {container} for item {grant.Item.ItemId}.");
                if (added <= 0 || added > outstanding)
                    throw new InvalidOperationException("A CERA grant did not consume a positive bounded quantity.");
                outstanding -= added;
                slots[slot] = new(slot, core, placement.WorldClass);
                writes[(container, slot)] = new(container, slot, core, facts);
            }
        }
        return new(CeraShopRejection.None, new(after, checked((int)goldCost),
            writes.Values.OrderBy(value => value.Container).ThenBy(value => value.SlotIndex).ToImmutableArray()), null);
    }

    private async ValueTask ExpandAsync(CeraShopItemDefinition item, int count, int expiration, byte directAbility,
        bool packageChoice, Choices choices, HashSet<int> ancestors, ImmutableArray<CeraShopGrant>.Builder grants,
        int level, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (count <= 0 || !item.Diagnostics.IsEmpty || item.Kind is CeraShopItemKind.Unknown or CeraShopItemKind.Service)
            throw Refuse(CeraShopRejection.UnsupportedProduct, $"Item {item.ItemId}: service or {Diagnostics(item.Diagnostics)}.");
        if (item.MinimumPurchaseLevel is { } minimum && level < minimum)
            throw Refuse(CeraShopRejection.PurchaseLevel, $"Item {item.ItemId} requires purchase level {minimum}.");
        if (ancestors.Count >= 8 || !ancestors.Add(item.ItemId))
            throw Refuse(CeraShopRejection.UnsupportedProduct, "Cyclic or excessively nested package.");
        if (item.Kind == CeraShopItemKind.Package)
        {
            var components = item.Components.ToBuilder();
            for (var group = 0; group < item.SelectionGroups.Length; group++)
                components.Add(choices.Select(item.ItemId, group, item.SelectionGroups[group]));
            if (components.Count == 0 || components.Count > 1024)
                throw Refuse(CeraShopRejection.UnsupportedProduct, "Empty or excessive package declaration.");
            foreach (var component in components)
            {
                var child = await content.GetItemAsync(component.ItemId, cancellationToken).ConfigureAwait(false)
                    ?? throw Refuse(CeraShopRejection.UnsupportedProduct, $"Missing package item {component.ItemId}.");
                var ownExpiry = Expiration(component.UsablePeriod, now);
                var expiry = expiration == 0 ? ownExpiry : ownExpiry == 0 ? expiration : Math.Min(expiration, ownExpiry);
                await ExpandAsync(child, checked(count * component.Count), expiry, 0, true, choices,
                    ancestors, grants, level, now, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            if (grants.Count >= 1024)
                throw Refuse(CeraShopRejection.UnsupportedProduct, "Too many expanded rewards.");
            var placement = await placements.PlacementForAsync(item.ItemId, cancellationToken).ConfigureAwait(false)
                ?? throw Refuse(CeraShopRejection.UnsupportedProduct, $"No placement for item {item.ItemId}.");
            byte ability = 0;
            if (item.Kind == CeraShopItemKind.Avatar)
            {
                if (item.Avatar is null || placement.WorldClass != ItemWorldClass.Avatar)
                    throw Refuse(CeraShopRejection.UnsupportedProduct, "Missing avatar declaration.");
                ability = packageChoice ? choices.Ability(item.ItemId) : directAbility;
                if (item.Avatar.SelectableAbilities.IsEmpty ? ability != 0 : !item.Avatar.SelectableAbilities.Contains(ability))
                    throw Refuse(CeraShopRejection.InvalidChoice, $"Item {item.ItemId} has no ability {ability}.");
                placement = placement with { ItemKind = 8, StackLimit = 1,
                    Destination = new(BusinessInventorySpace.Avatar, 0, 209) };
            }
            else if (item.Kind == CeraShopItemKind.Creature)
            {
                if (item.Creature is null || placement.WorldClass != ItemWorldClass.Creature || expiration != 0)
                    throw Refuse(CeraShopRejection.UnsupportedProduct, "Missing creature declaration or unsupported creature duration.");
                placement = placement with { ItemKind = 5, StackLimit = 1,
                    Destination = new(BusinessInventorySpace.CreatureInventory, 0, 139) };
            }
            else if (item.Kind == CeraShopItemKind.CreatureEquipment)
            {
                if (placement.WorldClass != ItemWorldClass.Equipment)
                    throw Refuse(CeraShopRejection.UnsupportedProduct, "Creature equipment is not an equipment instance.");
                placement = placement with { ItemKind = 6, StackLimit = 1,
                    Destination = new(BusinessInventorySpace.CreatureEquipment, 0, 48) };
            }
            else if (placement.ItemKind == 7)
                placement = placement with {
                    Destination = new(BusinessInventorySpace.CreatureConsumable, 0, 50) };
            if (placement.ItemKind == 0 || item.Kind == CeraShopItemKind.Stackable && placement.WorldClass != ItemWorldClass.Interchangeable
                || item.Kind == CeraShopItemKind.Equipment && placement.WorldClass != ItemWorldClass.Equipment)
                throw Refuse(CeraShopRejection.UnsupportedProduct, "Item declaration and placement disagree.");
            grants.Add(new(item, placement, count, ability, expiration));
        }
        ancestors.Remove(item.ItemId);
    }

    private InventoryContainerType ContainerFor(BusinessSlotRange destination) =>
        slotMappings.ToGameSlot(new BusinessSlot(destination.Space, destination.FirstIndex)).Space;

    private int StorageIndex(InventoryContainerType container, BusinessSlot slot)
    {
        var game = slotMappings.ToGameSlot(slot);
        if (game.Space != container)
            throw new InvalidDataException(
                $"CERA placement {slot.Space}/{slot.Index} does not belong to {container}.");
        return game.Index;
    }

    private static IEnumerable<BusinessSlot> CandidateSlots(ItemPlacement placement)
    {
        var own = Enumerable.Range(
                placement.Destination.FirstIndex,
                placement.Destination.LastIndex - placement.Destination.FirstIndex + 1)
            .Select(index => new BusinessSlot(placement.Destination.Space, index));
        return ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind)
            ? own.Concat(Enumerable.Range(0, 6)
                .Select(index => new BusinessSlot(BusinessInventorySpace.QuickSlot, index)))
            : own;
    }

    private static bool TryPay(AccountCeraBalances before, int cost, CeraShopPaymentMode mode, out AccountCeraBalances after)
    {
        var cera = before.Cera; var token = before.TokenCera; var happy = before.HappyTokenCera;
        var remaining = cost;
        if (mode != CeraShopPaymentMode.CeraOnly)
        {
            var take = Math.Min(remaining, happy); happy -= take; remaining -= take;
            take = Math.Min(remaining, token); token -= take; remaining -= take;
        }
        if (mode != CeraShopPaymentMode.PointsOnly)
        {
            var take = Math.Min(remaining, cera); cera -= take; remaining -= take;
        }
        after = new(cera, token, happy);
        return remaining == 0;
    }

    private static int Expiration(int days, DateTimeOffset now)
    {
        if (days < 0) throw Refuse(CeraShopRejection.UnsupportedProduct, "Negative item duration.");
        return days == 0 ? 0 : checked((int)(now.ToUnixTimeSeconds() + (long)days * 86400));
    }

    private static string Diagnostics(ImmutableArray<CeraShopDiagnostic> values) =>
        string.Join("; ", values.Select(value => $"{value.Code}: {value.Source}: {value.Detail}"));
    private static QuoteRefusal Refuse(CeraShopRejection reason, string detail) => new(reason, detail);
    private sealed class QuoteRefusal(CeraShopRejection reason, string detail) : Exception(detail)
    { public CeraShopRejection Reason { get; } = reason; }

    private sealed class Choices
    {
        private readonly Dictionary<int, byte> avatars = [];
        private readonly Dictionary<(int, int), ushort> selections = [];
        private readonly HashSet<int> usedAvatars = [];
        private readonly HashSet<(int, int)> usedSelections = [];

        public Choices(CeraShopPurchaseEntry entry)
        {
            foreach (var choice in entry.AvatarChoices)
                if (choice.ItemId <= 0 || !avatars.TryAdd(choice.ItemId, choice.Ability))
                    throw Refuse(CeraShopRejection.InvalidChoice, "Invalid or duplicate avatar option.");
            foreach (var choice in entry.Selections)
                if (choice.PackageItemId <= 0 || !selections.TryAdd((choice.PackageItemId, choice.GroupIndex), choice.SelectionIndex))
                    throw Refuse(CeraShopRejection.InvalidChoice, "Invalid or duplicate package selection.");
        }

        public byte Ability(int itemId)
        {
            if (avatars.Count == 0) return 0;
            if (!avatars.TryGetValue(itemId, out var ability))
                throw Refuse(CeraShopRejection.InvalidChoice, $"Missing avatar option for item {itemId}.");
            usedAvatars.Add(itemId);
            return ability;
        }

        public CeraShopComponent Select(int packageId, int group, CeraShopSelectionGroup declaration)
        {
            if (!selections.TryGetValue((packageId, group), out var index) || index >= declaration.Choices.Length)
                throw Refuse(CeraShopRejection.InvalidChoice, $"Missing or invalid selection for package {packageId}, group {group}.");
            usedSelections.Add((packageId, group));
            return declaration.Choices[index];
        }

        public void RequireAllUsed()
        {
            if (usedAvatars.Count != avatars.Count || usedSelections.Count != selections.Count)
                throw Refuse(CeraShopRejection.InvalidChoice, "Purchase contains unrelated options.");
        }
    }
}
