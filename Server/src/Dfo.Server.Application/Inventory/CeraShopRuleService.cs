using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class CeraShopRuleService(ICeraShopDefinitionSource content,
    IItemPlacementRuleService placements, IItemArrivalRuleService arrivals, IAccountWalletRuleService wallets,
    IInventoryExpansionDefinitionSource expansionContent,
    IItemExpirationRuleService expirationRules) : ICeraShopRuleService
{
    public async ValueTask<CeraShopQuoteResult> QuoteAsync(CeraShopPurchaseInput input, int characterLevel,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (input.InvalidReason is not null || input.Header0 != 0 || input.Header1 != 0 || input.CouponIndicator != 0
            || input.Entries.IsDefaultOrEmpty || input.Entries.Length > byte.MaxValue)
            return new(CeraShopRejection.InvalidRequest, null, input.InvalidReason ?? "Unsupported purchase header or cart size.");
        var products = ImmutableArray.CreateBuilder<int>();
        var charges = ImmutableArray.CreateBuilder<CeraShopCharge>();
        var grants = ImmutableArray.CreateBuilder<CeraShopGrant>();
        var expansions = ImmutableArray.CreateBuilder<InventoryExpansionGrant>();
        var subscriptions = ImmutableArray.CreateBuilder<CeraShopSubscriptionGrant>();
        var nameTags = ImmutableArray.CreateBuilder<Dfo.Server.Abstractions.Characters.NameTagPurchase>();
        var restrictions = ImmutableArray.CreateBuilder<CeraShopPurchaseRestrictionDeclaration>();
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
                if (product.PurchaseRestriction is { } restriction)
                {
                    if (restriction.RestrictionId <= 0 || restriction.CooldownDays < 0 || restriction.InitialFlag != 1)
                        throw Refuse(CeraShopRejection.UnsupportedProduct, "Unsupported purchase restriction declaration.");
                    if (restrictions.Any(r => r.RestrictionId == restriction.RestrictionId))
                        throw Refuse(CeraShopRejection.PurchaseRestricted, "A restricted product may occur only once in a cart.");
                    restrictions.Add(restriction);
                }
                if (product.IsService)
                {
                    if (!entry.AvatarChoices.IsEmpty || !entry.Selections.IsEmpty
                        || entry.Attribute is not (0 or 255) || entry.Variant is not (0 or 255))
                        throw Refuse(CeraShopRejection.InvalidChoice, "Unexpected subscription options.");
                    var expansion = await expansionContent.ProductAsync(
                        product.ProductId, product.ItemId, cancellationToken).ConfigureAwait(false);
                    if (expansion is not null && product.Count == 1 && price.Period == 0)
                        expansions.Add(expansion);
                    else if (product.Category == "[visual]" && product.Count == 1 && product.ServiceDays > 0)
                    {
                        var item = await content.GetItemAsync(product.ItemId, cancellationToken).ConfigureAwait(false);
                        if (item is not { IsNameTag: true } || !item.Diagnostics.IsEmpty)
                            throw Refuse(CeraShopRejection.UnsupportedProduct, "Visual product must declare name tag equipment.");
                        if (item.MinimumPurchaseLevel > characterLevel)
                            throw Refuse(CeraShopRejection.PurchaseLevel, "Name tag purchase level is not met.");
                        nameTags.Add(new(product.ItemId, product.ServiceDays));
                    }
                    else if (product.Category == "[premium]" && product.Count == 1
                        && product.Premium is { PremiumType: 22 or 27 or 84 or 97 } contract && contract.Duration > TimeSpan.Zero)
                    {
                        subscriptions.Add(new((PremiumServiceType)contract.PremiumType, contract.Duration));
                    }
                    else if (product.Category == "[selectable character premium]" && product.ServiceSlot is >= 0 and < 8
                        && product.ServiceDays is 7 or 30 && product.Count == 1)
                    {
                        subscriptions.Add(new(CharacterService(product.ServiceSlot.Value), TimeSpan.FromDays(product.ServiceDays)));
                    }
                    else if (product.Category == "[charac premium package]" && product.Count == 1 && price.Period == 0
                        && product.ServiceDays is 30 or 60 && !product.ServiceSlots.IsDefaultOrEmpty
                        && product.ServiceSlots.Length <= 8 && product.ServiceSlots.All(slot => slot is >= 0 and < 8)
                        && product.ServiceSlots.Distinct().Count() == product.ServiceSlots.Length)
                    {
                        foreach (var slot in product.ServiceSlots)
                            subscriptions.Add(new(CharacterService(slot), TimeSpan.FromDays(product.ServiceDays)));
                    }
                    else throw Refuse(CeraShopRejection.UnsupportedProduct, "Unsupported subscription declaration.");
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
            return new(CeraShopRejection.None, new(products.ToImmutable(), charges.ToImmutable(), grants.ToImmutable())
                { Expansions = expansions.ToImmutable(), Subscriptions = subscriptions.ToImmutable(), NameTags = nameTags.ToImmutable(), Restrictions = restrictions.ToImmutable() }, null);
        }
        catch (QuoteRefusal failure) { return new(failure.Reason, null, failure.Message); }
        catch (OverflowException) { return new(CeraShopRejection.UnsupportedProduct, null, "Purchase quantity or expiration cannot be represented."); }
    }

    public CeraShopRestrictionPlanningResult PlanRestrictions(CeraShopQuote quote, CeraShopRestrictionSnapshot state, DateTimeOffset now)
    {
        var effects = new List<DomainEffect>();
        var seen = new HashSet<int>();
        try
        {
            foreach (var declaration in quote.Restrictions)
            {
                if (declaration.RestrictionId <= 0 || declaration.CooldownDays is < 0 or > byte.MaxValue || declaration.InitialFlag != 1)
                    return new(CeraShopRejection.UnsupportedProduct, null, "Unsupported purchase restriction declaration.");
                if (!seen.Add(declaration.RestrictionId))
                    return new(CeraShopRejection.PurchaseRestricted, null, "Duplicate purchase restriction in cart.");
                var current = state.Entries.SingleOrDefault(r => r.EntryId == declaration.RestrictionId);
                if (current is { Flag: 0 })
                    return new(CeraShopRejection.PurchaseRestricted, null, $"Restriction {declaration.RestrictionId} remains active until {current.SentinelOrValue}.");
                var end = declaration.CooldownDays == 0 ? CeraShopPurchaseRestrictionDomainService.PermanentEnd
                    : checked((int)now.AddDays(declaration.CooldownDays).ToUnixTimeSeconds());
                if (end <= now.ToUnixTimeSeconds() || end > CeraShopPurchaseRestrictionDomainService.PermanentEnd)
                    return new(CeraShopRejection.UnsupportedProduct, null, "Restriction expiration cannot be represented.");
                effects.Add(new SetPurchaseRestrictionEffect(declaration.RestrictionId,
                    state.Stored.SingleOrDefault(r => r.RestrictionId == declaration.RestrictionId)?.EndsAtUnixSeconds, end));
            }
            return new(CeraShopRejection.None, effects, null);
        }
        catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException)
        { return new(CeraShopRejection.UnsupportedProduct, null, "Restriction expiration cannot be represented."); }
    }

    public CeraShopPlanningResult Plan(CeraShopQuote quote,
        IReadOnlyDictionary<BusinessInventorySpace, IReadOnlyList<ItemReference>> held,
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

        var standing = held.ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(item => item.SlotIndex, item => item.Snapshot));
        var writes = new Dictionary<BusinessSlot, CeraShopSlotWrite>();
        var serial = 0L;
        foreach (var grant in quote.Grants)
        {
            var placement = grant.Placement;
            if (!standing.TryGetValue(placement.Destination.Space, out _))
                return new(CeraShopRejection.NoSession, null,
                    $"Container {placement.Destination.Space} is not bound.");
            var baseline = new ItemSnapshot
            {
                ItemId = grant.Item.ItemId, Count = 0, Type = placement.Traits, LegacyKind = placement.ItemKind,
                IsPile = placement.WorldClass == ItemWorldClass.Interchangeable, Attribute = grant.Ability,
                Durability = grant.Item.Durability,
                SealFlag = grant.Item.Kind == CeraShopItemKind.Creature ? (byte)0 : grant.Item.Sealed ? (byte)1 : (byte)0,
                ExpireTime = grant.Item.Kind == CeraShopItemKind.Avatar ? 0 : grant.ExpireDate,
            };
            var outstanding = grant.Count;
            while (outstanding > 0)
            {
                var candidates = CandidateSlots(placement).ToArray();
                if (candidates.Any(slot => !standing.ContainsKey(slot.Space)))
                    return new(CeraShopRejection.NoSession, null, "A candidate container is not bound.");
                var businessPiles = candidates
                    .Where(slot => standing[slot.Space].TryGetValue(slot.Index, out var item)
                        && item.IsPile
                        && item.ItemId == grant.Item.ItemId
                        && item.CanStackWith(baseline))
                    .Select(slot => new ItemPile(slot, standing[slot.Space][slot.Index].Count)).ToArray();
                var arrival = arrivals.ForGrant(businessPiles, outstanding, placement,
                    candidates.Where(slot => standing[slot.Space].ContainsKey(slot.Index))
                        .ToHashSet());
                BusinessSlot slot;
                int added;
                ItemSnapshot core;
                if (arrival is ItemArrival.OntoExistingPile pile)
                {
                    slot = pile.Slot;
                    added = pile.Added;
                    core = standing[slot.Space][slot.Index] with { Count = pile.Total };
                }
                else if (arrival is ItemArrival.IntoEmptySlot empty)
                {
                    slot = empty.Slot;
                    added = empty.Count;
                    var value = placement.WorldClass == ItemWorldClass.Interchangeable ? added
                        : (int)(((long)(uint)identitySeed + serial++) % (int.MaxValue - 1)) + 1;
                    core = baseline with { Count = baseline.IsPile ? added : 1, InstanceValue = baseline.IsPile ? 0 : value };
                    core = grant.Item.Kind switch
                    {
                        CeraShopItemKind.Avatar => core with { AvatarDetail = new(grant.ExpireDate,
                            0, new byte[30], 0, 0, 0) },
                        // Initial satiety and flags follow the reference creation policy; level is PVF data.
                        CeraShopItemKind.Creature => core with { CreatureDetail = new(100, 0, 0, 0, 0,
                            grant.Item.Creature!.StartLevel, [], 0) },
                        _ => core,
                    };
                }
                else return new(CeraShopRejection.NoRoom, null,
                    $"No room in {placement.Destination.Space} for item {grant.Item.ItemId}.");
                if (added <= 0 || added > outstanding)
                    throw new InvalidOperationException("A CERA grant did not consume a positive bounded quantity.");
                outstanding -= added;
                standing[slot.Space][slot.Index] = core;
                writes[slot] = new(slot, core);
            }
        }
        return new(CeraShopRejection.None, new(after, checked((int)goldCost),
            writes.Values.OrderBy(value => value.Slot.Space).ThenBy(value => value.Slot.Index).ToImmutableArray()), null);
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
            var declared = expirationRules.Prepare(new ItemSnapshot(), placement.Lifetime, now).ExpireTime;
            if (declared > 0) expiration = expiration == 0 ? declared : Math.Min(expiration, declared);
            byte ability = 0;
            if (item.Kind == CeraShopItemKind.Avatar)
            {
                if (item.Avatar is null || placement.WorldClass != ItemWorldClass.Avatar
                    || placement.ItemKind != 8 || placement.Destination.Space != BusinessInventorySpace.Avatar)
                    throw Refuse(CeraShopRejection.UnsupportedProduct, "Missing avatar declaration.");
                ability = packageChoice ? choices.Ability(item.ItemId) : directAbility;
                if (item.Avatar.SelectableAbilities.IsEmpty ? ability != 0 : !item.Avatar.SelectableAbilities.Contains(ability))
                    throw Refuse(CeraShopRejection.InvalidChoice, $"Item {item.ItemId} has no ability {ability}.");
            }
            else if (item.Kind == CeraShopItemKind.Creature)
            {
                if (item.Creature is null || placement.WorldClass != ItemWorldClass.Creature
                    || placement.ItemKind != 5 || placement.Destination.Space != BusinessInventorySpace.CreatureInventory
                    || expiration != 0)
                    throw Refuse(CeraShopRejection.UnsupportedProduct, "Missing creature declaration or unsupported creature duration.");
            }
            else if (item.Kind == CeraShopItemKind.CreatureEquipment)
            {
                if (placement.WorldClass != ItemWorldClass.Equipment || placement.ItemKind != 6
                    || placement.Destination.Space != BusinessInventorySpace.CreatureEquipment)
                    throw Refuse(CeraShopRejection.UnsupportedProduct, "Creature equipment is not an equipment instance.");
            }
            else if (placement.ItemKind == 7
                && placement.Destination.Space != BusinessInventorySpace.CreatureConsumable)
                throw Refuse(CeraShopRejection.UnsupportedProduct, "Creature feed placement disagrees with the item definition.");
            if (placement.ItemKind == 0 || item.Kind == CeraShopItemKind.Stackable && placement.WorldClass != ItemWorldClass.Interchangeable
                || item.Kind == CeraShopItemKind.Equipment && placement.WorldClass != ItemWorldClass.Equipment)
                throw Refuse(CeraShopRejection.UnsupportedProduct, "Item declaration and placement disagree.");
            grants.Add(new(item, placement, count, ability, expiration));
        }
        ancestors.Remove(item.ItemId);
    }

    private static IEnumerable<BusinessSlot> CandidateSlots(ItemPlacement placement)
    {
        var own = Enumerable.Range(
                placement.Destination.FirstIndex,
                placement.Destination.LastIndex - placement.Destination.FirstIndex + 1)
            .Select(index => new BusinessSlot(placement.Destination.Space, index));
        return placement.Destination.Space != BusinessInventorySpace.SharedMaterial && ItemSlotRuleService.AllowsQuickSlot(placement)
            ? own.Concat(Enumerable.Range(0, 6)
                .Select(index => new BusinessSlot(BusinessInventorySpace.QuickSlot, index)))
            : own;
    }

    private bool TryPay(AccountCeraBalances before, int cost, CeraShopPaymentMode mode, out AccountCeraBalances after)
    {
        after = before;
        var remaining = cost;
        if (mode != CeraShopPaymentMode.CeraOnly)
        {
            var take = Math.Min(remaining, after.HappyTokenCera);
            if (take > 0) after = wallets.Spend(after, AccountCurrency.HappyTokenCera, take);
            remaining -= take;
            take = Math.Min(remaining, after.TokenCera);
            if (take > 0) after = wallets.Spend(after, AccountCurrency.TokenCera, take);
            remaining -= take;
        }
        if (mode != CeraShopPaymentMode.PointsOnly)
        {
            var take = Math.Min(remaining, after.Cera);
            if (take > 0) after = wallets.Spend(after, AccountCurrency.Cera, take);
            remaining -= take;
        }
        return remaining == 0;
    }

    private static int Expiration(int days, DateTimeOffset now)
    {
        if (days < 0) throw Refuse(CeraShopRejection.UnsupportedProduct, "Negative item duration.");
        return days == 0 ? 0 : checked((int)(now.ToUnixTimeSeconds() + (long)days * 86400));
    }

    private static PremiumServiceType CharacterService(int slot) => slot switch
    {
        0 => PremiumServiceType.FreeGoldCard, 1 => PremiumServiceType.DoublePot,
        2 => PremiumServiceType.QuestHelper, 3 => PremiumServiceType.DungeonBuff,
        4 => PremiumServiceType.AutoHpMpRecovery, 5 => PremiumServiceType.FreeWeaknessRecovery,
        6 => PremiumServiceType.AutoRepair, 7 => PremiumServiceType.FastPot,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

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
