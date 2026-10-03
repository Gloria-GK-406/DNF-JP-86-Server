using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Creatures;

namespace Dfo.Server.Application.Inventory;

internal sealed class CeraShopDomainService(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers, ICharacterDomainService characters,
    ICharacterTable characterRows, IAccountTable accounts, ICharacterBusinessInventoryDomainService stores,
    ICeraShopRuleService rules, IItemLifecycleDomainService lifecycle, IItemDomainStore itemStore, IDomainTransactionAccessor transactions,
    ICharacterInventoryEntryDomainService inventory, ICharacterCreatureTable creatures, IPremiumServiceDomainService premiums, IPremiumServiceUsageDomainService premiumUsage,
    ICeraShopPurchaseRestrictionDomainService restrictions, IApplyEffectsDomainService effects,
    ICreatureDomainService? creatureDomains = null, IInventoryExpansionDomainService? expansion = null,
    IInventoryCapacityRuleService? capacityRules = null) : ICeraShopDomainService
{
    public async ValueTask<CeraShopPurchaseResult> PurchaseAsync(SelectedSessionIdentity session, CeraShopPurchaseInput input,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var transaction = transactions.Current
            ?? throw new InvalidOperationException("CERA purchases require a transaction boundary.");
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId
            || !await characterRows.BelongsToAsync(session.CharacterId, session.AccountId, cancellationToken).ConfigureAwait(false))
            return CeraShopPurchaseResult.Refused(CeraShopRejection.NoSession);

        var character = characters.Get(session.CharacterId);
        var progress = await character.ProgressAsync(cancellationToken).ConfigureAwait(false);
        var wallet = await accounts.FindWalletAsync(session.AccountId, cancellationToken).ConfigureAwait(false);
        if (progress is null || wallet is null)
            return CeraShopPurchaseResult.Refused(CeraShopRejection.NoSession);
        var quoted = await rules.QuoteAsync(input, progress.Level, now, cancellationToken).ConfigureAwait(false);
        if (quoted.Quote is not { } quote)
            return CeraShopPurchaseResult.Refused(quoted.Rejection, quoted.Detail);
        IReadOnlyList<DomainEffect> restrictionEffects = [];
        if (!quote.Restrictions.IsEmpty)
        {
            var restrictionPlan = rules.PlanRestrictions(quote,
                await restrictions.ReadAsync(session.AccountId, now, cancellationToken).ConfigureAwait(false), now);
            if (restrictionPlan.Effects is null)
                return CeraShopPurchaseResult.Refused(restrictionPlan.Rejection, restrictionPlan.Detail);
            restrictionEffects = restrictionPlan.Effects;
        }

        IReadOnlyList<InventoryExpansionChange> capacityChanges = [];
        if (!quote.Expansions.IsEmpty)
        {
            var changes = expansion is null ? null : await expansion.PlanPurchaseAsync(session, quote.Expansions, cancellationToken).ConfigureAwait(false);
            if (changes is null) return CeraShopPurchaseResult.Refused(CeraShopRejection.StateChanged, "Expansion stage is unavailable.");
            capacityChanges = changes;
        }
        var bags = new Dictionary<BusinessInventorySpace, IBusinessItemContainer>();
        var held = new Dictionary<BusinessInventorySpace, IReadOnlyList<ItemReference>>();
        var spaces = quote.Grants.Select(grant => grant.Placement.Destination.Space)
            .Concat(quote.Grants.Where(grant => grant.Placement.Destination.Space != BusinessInventorySpace.SharedMaterial && ItemSlotRuleService.AllowsQuickSlot(grant.Placement))
                .Select(_ => BusinessInventorySpace.QuickSlot)).Distinct();
        foreach (var space in spaces)
        {
            var bag = stores.GetStore(session.CharacterId, BusinessInventoryOwnership.StoreOf(space))
                .FindContainer(space);
            if (bag is null) continue;
            bags.Add(space, bag);
            held.Add(space, await bag.GetAllAsync(cancellationToken).ConfigureAwait(false));
        }
        var boundedGrants = ImmutableArray.CreateBuilder<CeraShopGrant>();
        foreach (var grant in quote.Grants)
        {
            if (!bags.TryGetValue(grant.Placement.Destination.Space, out var bag))
                return CeraShopPurchaseResult.Refused(CeraShopRejection.NoRoom);
            var space = grant.Placement.Destination.Space;
            var open = await bag.OpenSlotCountAsync(cancellationToken).ConfigureAwait(false);
            var kind = space == BusinessInventorySpace.Avatar ? Dfo.Server.Abstractions.Content.InventoryExpansionKind.Avatar
                : Dfo.Server.Abstractions.Content.InventoryExpansionKind.Main;
            var changedCapacity = capacityChanges.LastOrDefault(c => c.Kind == kind);
            if (changedCapacity is not null && capacityRules is not null)
                open = capacityRules.OpenSlotCount(space, changedCapacity.Parameter);
            var placement = InventoryCapacityPlacement.Limit(grant.Placement, open);
            if (placement is null) return CeraShopPurchaseResult.Refused(CeraShopRejection.NoRoom);
            boundedGrants.Add(grant with { Placement = placement });
        }
        quote = quote with { Grants = boundedGrants.ToImmutable() };
        var gold = await character.GoldAsync(cancellationToken).ConfigureAwait(false);
        var before = new AccountCeraBalances(checked((int)wallet.Cera), checked((int)wallet.TokenCera), checked((int)wallet.HappyTokenCera));
        var planned = rules.Plan(quote, held, gold, before, Random.Shared.Next(1, int.MaxValue));
        if (planned.Plan is not { } plan)
            return CeraShopPurchaseResult.Refused(planned.Rejection, planned.Detail);

        // From the first write onwards, every refusal must unwind the shared transaction.
        if (before != plan.Balances && await accounts.TrySetCeraBalancesAsync(session.AccountId, before, plan.Balances,
                cancellationToken).ConfigureAwait(false) != 1)
            throw new CeraShopPurchaseRejectedException(CeraShopRejection.StateChanged, "Account balances changed before settlement.");
        if (plan.GoldCost > 0)
            gold = await character.TrySpendGoldAsync(plan.GoldCost, cancellationToken).ConfigureAwait(false)
                ?? throw new CeraShopPurchaseRejectedException(CeraShopRejection.InsufficientFunds, "Gold changed before settlement.");
        if (restrictionEffects.Count > 0)
            await effects.ApplyAsync(new(session.AccountId, session.CharacterId, now, session), restrictionEffects,
                cancellationToken).ConfigureAwait(false);
        if (capacityChanges.Count > 0)
            await expansion!.ApplyPurchaseAsync(session, capacityChanges, cancellationToken).ConfigureAwait(false);
        foreach (var subscription in quote.Subscriptions)
            await premiums.For(session.AccountId).ExtendAsync(subscription.Service, subscription.Duration,
                cancellationToken).ConfigureAwait(false);
        if (quote.Subscriptions.Any(subscription => subscription.Service == PremiumServiceType.DoublePot))
            await premiumUsage.RenewDoublePotAsync(session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        foreach (var write in plan.Writes)
        {
            var bag = bags[write.Slot.Space];
            var existing = await bag.GetAsync(write.Slot.Index, cancellationToken).ConfigureAwait(false);
            if (existing is null)
                await bag.PutAsync(write.Slot.Index, await lifecycle.CreateAsync(write.Core, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            else
            {
                if (!existing.Snapshot.CanStackWith(write.Core) || write.Core.Count < existing.Count)
                    throw new InvalidOperationException("CERA grant target changed after planning.");
                await bag.UpdateAsync(existing, current => current with { Count = write.Core.Count }, cancellationToken).ConfigureAwait(false);
            }
        }
        await itemStore.SessionIn(transaction).ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);

        // Read through the entry projection: it validates ownership and carries avatar facts and
        // newly allocated world identities, which a pre-write plan cannot truthfully supply.
        var snapshot = await inventory.LoadAsync(session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        var updates = ImmutableArray.CreateBuilder<CommittedInventoryUpdateState>();
        foreach (var group in plan.Writes.GroupBy(write =>
            LegacyInventoryStorageCoordinates.ToStorage(write.Slot).List).OrderBy(group => group.Key))
        {
            var changedSlots = group.Select(write =>
                LegacyInventoryStorageCoordinates.ToStorage(write.Slot).Index).ToHashSet();
            var items = snapshot.Containers.Single(container => container.ContainerType == group.Key).Items
                .Where(item => changedSlots.Contains(item.SlotIndex)).OrderBy(item => item.SlotIndex).ToArray();
            if (items.Length != changedSlots.Count)
                throw new InvalidDataException("A committed CERA grant is absent from its inventory projection.");
            updates.Add(new(group.Key, items, now));
        }
        var hasCreatures = plan.Writes.Any(write => write.Core.CreatureDetail is not null);
        ImmutableArray<CharacterPetRuntimeState> pets = [];
        if (hasCreatures)
        {
            if (creatureDomains is not null)
            {
                var states = await creatureDomains.GetStore(session.CharacterId)
                    .ReadAllAsync(cancellationToken).ConfigureAwait(false);
                if (states.Count > byte.MaxValue)
                    throw new CeraShopPurchaseRejectedException(CeraShopRejection.NoRoom, "Creature list cannot fit its wire count.");
                pets = states.Select(CreatureRuntimeProjection.Build).ToImmutableArray();
            }
            else
            {
                var rows = await creatures.ReadAsync(session.CharacterId, cancellationToken).ConfigureAwait(false);
                if (rows.Count > byte.MaxValue)
                    throw new CeraShopPurchaseRejectedException(CeraShopRejection.NoRoom, "Creature list cannot fit its wire count.");
                pets = rows.OrderBy(row => row.SortOrder).Select(row => new CharacterPetRuntimeState(row.CreatureKey,
                    row.Field04, checked((byte)row.ModeFlag), row.ProgressValue, row.Mode1Field0A, row.Mode1Field0B,
                    row.FieldAfterValue, [.. row.CreatureText], checked((byte)row.TailFlag))).ToImmutableArray();
            }
        }
        AccountEntitlementEntryState? entitlements = null;
        IReadOnlyList<CharacterPremiumUsageSlotState> usage = [];
        if (!quote.Subscriptions.IsEmpty)
        {
            entitlements = new(await premiums.For(session.AccountId).ReadAsync(cancellationToken).ConfigureAwait(false));
            usage = await premiumUsage.ReadAsync(session.CharacterId, cancellationToken).ConfigureAwait(false);
        }
        var expandedInventory = capacityChanges.Count == 0 ? null
            : await expansion!.SnapshotAsync(session, now, cancellationToken).ConfigureAwait(false);
        return new(CeraShopRejection.None, new(session, quote.ProductIds, plan.Balances, gold, plan.GoldCost > 0,
            updates.ToImmutable(), pets, hasCreatures, now)
            { ExpandedCharacterInventory = expandedInventory?.Character, ExpandedAccountInventory = expandedInventory?.Account,
                ExpandedContainers = capacityChanges.Select(c => c.Kind switch
                {
                    Dfo.Server.Abstractions.Content.InventoryExpansionKind.Main => InventoryContainerType.Main,
                    Dfo.Server.Abstractions.Content.InventoryExpansionKind.Avatar => InventoryContainerType.Avatar,
                    Dfo.Server.Abstractions.Content.InventoryExpansionKind.PersonalCargo => InventoryContainerType.PersonalCargo,
                    _ => InventoryContainerType.AccountCargo,
                }).Distinct().ToArray(),
                PremiumEntitlements = entitlements, PremiumUsageSlots = usage,
                PurchaseRestrictions = quote.Restrictions.IsEmpty ? null
                    : (await restrictions.ReadAsync(session.AccountId, now, cancellationToken).ConfigureAwait(false)).Entries,
                GrantedPremiums = entitlements?.Premiums.Where(p => p.PremiumType is 22 or 27 or 84 or 97
                    && quote.Subscriptions.Any(s => (int)s.Service == p.PremiumType)).ToArray() ?? [] }, null);
    }
}
