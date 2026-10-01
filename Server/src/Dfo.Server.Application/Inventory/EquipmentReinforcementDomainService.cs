using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentReinforcementDomainService(
    ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers,
    IBusinessInventorySlotDomainService slots,
    IEquipmentDomainService equipments,
    IEquipmentUpgradeRuleService rules,
    IEquipmentReinforcementFailureRuleService failures,
    ICharacterDomainService characters,
    IAccountMaterialBagDomainService accountMaterials,
    IAccountMaterialRuleService accountMaterialRules,
    IMaterialConsumptionDomainService materials,
    ICharacterContainerDomainService containers,
    IItemPlacementRuleService placements,
    IItemArrivalRuleService arrivals,
    IEquipmentReinforcementRollSource rolls,
    TimeProvider timeProvider) : IEquipmentReinforcementDomainService
{
    public async ValueTask<EquipmentReinforcementResult> ReinforceAsync(
        SelectedSessionIdentity session, BusinessSlot targetSlot, int expectedItemId,
        BusinessSlot? materialSlot, int? accountMaterialItemId,
        BusinessSlot? protectionSlot,
        CancellationToken ct, EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentReinforcementRejection.NoSession);
        if (!targetSlot.Space.IsCarriedItemSpace() || expectedItemId <= 0
            || materialSlot is { } candidateMaterial
                && (!candidateMaterial.Space.IsCarriedItemSpace() || candidateMaterial == targetSlot)
            || materialSlot is not null && accountMaterialItemId is not null
            || protectionSlot is { } protection && (!protection.Space.IsCarriedItemSpace()
                || protection == targetSlot || protection == materialSlot))
            return new(EquipmentReinforcementRejection.InvalidRequest);

        var instance = await equipments.Of(session.CharacterId).FindAtAsync(targetSlot, ct).ConfigureAwait(false);
        var target = instance is null ? null : await instance.ReadAsync(ct).ConfigureAwait(false);
        if (target is null) return new(EquipmentReinforcementRejection.ItemUnavailable);
        if (target.Core.ItemId != expectedItemId) return new(EquipmentReinforcementRejection.WrongItem);
        if (target.Core.EquipmentLockId != 0 || target.Core.SortLockFlag != 0)
            return new(EquipmentReinforcementRejection.Locked);
        var quote = await rules.QuoteReinforcementAsync(target.Core, ct, kind).ConfigureAwait(false);
        if (quote.Rejection != EquipmentReinforcementRejection.None) return new(quote.Rejection);
        var rule = quote.Rule ?? throw new InvalidOperationException("Reinforcement quote lacks its rule.");
        var oldLevel = target.Core.ReinforcementLevel;
        var goldCost = rule.GoldCost;

        var accountKind = accountMaterialRules.TryGetByItemId(rule.MaterialItemId, out var accountDefinition)
            ? accountDefinition.Kind : (AccountMaterialKind?)null;
        MaterialLocation materialLocation;
        if (accountMaterialItemId is { } requestedAccountMaterial)
        {
            if (requestedAccountMaterial != rule.MaterialItemId || accountKind is not { } resourceKind)
                return new(EquipmentReinforcementRejection.InsufficientMaterial);
            var accountCount = (await accountMaterials.Get(session.AccountId)
                .SnapshotAsync(ct).ConfigureAwait(false)).Count(resourceKind);
            if (accountCount < rule.MaterialCount)
                return new(EquipmentReinforcementRejection.InsufficientMaterial);
            materialLocation = new MaterialLocation.Account(resourceKind);
        }
        else if (materialSlot is { } sourceSlot)
        {
            var materialBound = slots.Find(session.CharacterId, sourceSlot);
            var material = materialBound is null ? null
                : await materialBound.Container.ReadSlotAsync(materialBound.Slot.Index, ct).ConfigureAwait(false);
            if (material is not { IsPile: true } || material.ItemId != rule.MaterialItemId
                || material.Count < rule.MaterialCount)
                return new(EquipmentReinforcementRejection.InsufficientMaterial);
            materialLocation = new MaterialLocation.Carried(sourceSlot);
        }
        else return new(EquipmentReinforcementRejection.InsufficientMaterial);

        EquipmentReinforcementProtectionDefinition? protectionDefinition = null;
        ContainerSlotItem? protectionItem = null;
        if (protectionSlot is { } ticketSlot)
        {
            var ticketBound = slots.Find(session.CharacterId, ticketSlot);
            protectionItem = ticketBound is null ? null
                : await ticketBound.Container.ReadSlotAsync(ticketBound.Slot.Index, ct).ConfigureAwait(false);
            if (protectionItem is not { IsPile: true, Count: > 0 }
                || protectionItem.Core.ExpireTime > 0
                    && protectionItem.Core.ExpireTime <= timeProvider.GetUtcNow().ToUnixTimeSeconds())
                return new(EquipmentReinforcementRejection.InvalidRequest);
            protectionDefinition = await rule.ProtectionAsync(protectionItem.ItemId, ct)
                .ConfigureAwait(false);
            if (protectionDefinition is null)
                return new(EquipmentReinforcementRejection.InvalidRequest);
        }

        var character = characters.Get(session.CharacterId);
        if (await character.IdentityAsync(ct).ConfigureAwait(false) is null)
            return new(EquipmentReinforcementRejection.NoSession);
        if (await character.GoldAsync(ct).ConfigureAwait(false) < goldCost)
            return new(EquipmentReinforcementRejection.InsufficientGold);

        if (rule.CanUseAutomaticProtection && protectionSlot is null)
        {
            var automatic = await FindAutomaticProtectionAsync(session, rule, ct).ConfigureAwait(false);
            if (automatic is { } selected)
            {
                protectionSlot = selected.Slot;
                protectionItem = selected.Item;
                protectionDefinition = selected.Definition;
            }
        }
        var succeeded = rule.TryReinforce(rolls.Next(100_000));
        var decision = succeeded
            ? new EquipmentReinforcementFailureResult(EquipmentReinforcementRejection.None,
                EquipmentReinforcementOutcome.Succeeded, checked((byte)(oldLevel + 1)), false, [])
            : await failures.GetResultAsync(instance!,
                new(protectionDefinition, rolls.Next(10_000), rolls.Next(10_000), kind), ct).ConfigureAwait(false);
        if (decision.Rejection != EquipmentReinforcementRejection.None) return new(decision.Rejection);
        var rewardRejection = await ValidateRewardsAsync(session, targetSlot, decision.Rewards,
            materialLocation, rule.MaterialCount, protectionSlot, decision.ConsumeProtection, ct).ConfigureAwait(false);
        if (rewardRejection != EquipmentReinforcementRejection.None) return new(rewardRejection);

        var costs = new List<MaterialCost>
        {
            new(materialLocation, rule.MaterialItemId, rule.MaterialCount),
        };
        if (decision.ConsumeProtection && protectionSlot is { } usedTicket)
            costs.Add(new(new MaterialLocation.Carried(usedTicket), protectionItem!.ItemId, 1));
        var debited = await materials.ConsumeAsync(session, costs, ct).ConfigureAwait(false);
        if (!debited.Success)
            return new(EquipmentReinforcementRejection.InsufficientMaterial);
        var gold = await character.TrySpendGoldAsync(goldCost, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Validated reinforcement gold changed during settlement.");
        InventoryItemCoreState? updated = null;
        if (decision.Outcome == EquipmentReinforcementOutcome.Destroyed)
            await instance!.DestroyAsync(ct).ConfigureAwait(false);
        else
        {
            updated = (await instance!.SetReinforcementLevelAsync(decision.NewLevel, ct).ConfigureAwait(false)).Core;
        }
        var changes = new List<EquipmentReinforcementSlotChange> { new(targetSlot, updated) };
        foreach (var debit in debited.Debits)
            if (debit.Cost.Location is MaterialLocation.Carried carried)
                changes.Add(new(carried.Slot, debit.Remaining));
        var accountChanges = debited.Debits
            .Select(debit => debit.AccountChange).OfType<AccountMaterialChange>().ToArray();
        var rewards = new List<EquipmentReinforcementReward>();
        foreach (var reward in decision.Rewards)
        {
            // All quantities are preplanned; staging and publication remain under the command transaction.
            var addition = await containers.AddAsync(session.CharacterId, reward.ItemId, reward.Count, ct)
                .ConfigureAwait(false);
            foreach (var item in addition.Items)
            {
                rewards.Add(new(item.Slot, reward.ItemId, item.Count));
                var bound = slots.Get(session.CharacterId, item.Slot);
                var final = await bound.Container.ReadSlotAsync(bound.Slot.Index, ct).ConfigureAwait(false);
                changes.Add(new(item.Slot, final?.Core));
            }
            if (addition.AccountMaterial is { } addedAccount)
            {
                accountChanges = [.. accountChanges, addedAccount];
                rewards.Add(new(null, reward.ItemId, reward.Count, addedAccount.VirtualSlotIndex));
            }
        }
        var materialRemaining = debited.Debits[0] is { AccountChange: { } accountChange }
            ? accountChanges.Last(change => change.Kind == accountChange.Kind).FinalCount
            : debited.Debits[0].Remaining?.Value ?? 0;
        if (materialLocation is MaterialLocation.Carried materialSource)
        {
            var bound = slots.Get(session.CharacterId, materialSource.Slot);
            var final = await bound.Container.ReadSlotAsync(bound.Slot.Index, ct).ConfigureAwait(false);
            materialRemaining = final is { IsPile: true } && final.ItemId == rule.MaterialItemId ? final.Count : 0;
        }
        var receipt = new EquipmentReinforcementReceipt(session, targetSlot, materialSlot,
            decision.ConsumeProtection ? protectionSlot : null, oldLevel, decision.NewLevel,
            decision.Outcome, materialRemaining, gold, changes, rewards, accountChanges,
            timeProvider.GetUtcNow());
        return new(EquipmentReinforcementRejection.None, receipt);
    }

    private async ValueTask<(BusinessSlot Slot, ContainerSlotItem Item,
        EquipmentReinforcementProtectionDefinition Definition)?> FindAutomaticProtectionAsync(
        SelectedSessionIdentity session, IEquipmentReinforcementRule rule, CancellationToken ct)
    {
        var available = new Dictionary<BusinessSlot, ContainerSlotItem>();
        foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Material })
        {
            var bound = slots.Find(session.CharacterId, new(space, 0));
            if (bound is null) continue;
            foreach (var item in await bound.Container.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (!item.IsPile || item.Count <= 0 || item.Core.ExpireTime > 0
                    && item.Core.ExpireTime <= timeProvider.GetUtcNow().ToUnixTimeSeconds()) continue;
                available.Add(new(space, item.SlotIndex), item);
            }
        }
        var choice = await rule.SelectAutomaticProtectionAsync(
            available.Select(pair => new EquipmentProtectionCandidate(pair.Key, pair.Value.ItemId)).ToArray(), ct)
            .ConfigureAwait(false);
        return choice is null ? null : (choice.Slot, available[choice.Slot], choice.Definition);
    }

    private async ValueTask<EquipmentReinforcementRejection> ValidateRewardsAsync(SelectedSessionIdentity session,
        BusinessSlot targetSlot, IReadOnlyList<EquipmentReinforcementFailureReward> rewards,
        MaterialLocation materialLocation, int materialCount, BusinessSlot? protectionSlot,
        bool consumeProtection, CancellationToken ct)
    {
        if (rewards.Count == 0) return EquipmentReinforcementRejection.None;
        var standing = new Dictionary<BusinessSlot, ContainerSlotItem>();
        var loaded = new HashSet<BusinessInventorySpace>();
        async ValueTask<bool> LoadAsync(BusinessInventorySpace space)
        {
            if (!loaded.Add(space)) return true;
            var bound = slots.Find(session.CharacterId, new(space, 0));
            if (bound is null) return false;
            foreach (var item in await bound.Container.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var slot = new BusinessSlot(space, item.SlotIndex);
                if (slot == targetSlot) continue;
                var remaining = item.Count;
                if (materialLocation is MaterialLocation.Carried carried && slot == carried.Slot)
                    remaining -= materialCount;
                if (consumeProtection && protectionSlot == slot) remaining--;
                if (remaining > 0) standing[slot] = remaining == item.Count
                    ? item : item with { Core = item.Core with { Value = remaining } };
            }
            return true;
        }
        var balances = new Dictionary<AccountMaterialKind, long>();
        var account = await accountMaterials.Get(session.AccountId).SnapshotAsync(ct).ConfigureAwait(false);
        var plannedSlots = 0;
        foreach (var reward in rewards)
        {
            if (reward.ItemId <= 0 || reward.Count <= 0) return EquipmentReinforcementRejection.UnsupportedOutcome;
            if (accountMaterialRules.TryGetByItemId(reward.ItemId, out var accountDefinition))
            {
                var count = balances.TryGetValue(accountDefinition.Kind, out var current) ? current
                    : (long)account.Count(accountDefinition.Kind)
                        - (materialLocation is MaterialLocation.Account consumed && consumed.Kind == accountDefinition.Kind
                            ? materialCount : 0);
                count += reward.Count;
                if (count is < 0 or > int.MaxValue || ++plannedSlots > byte.MaxValue)
                    return EquipmentReinforcementRejection.NoRoom;
                balances[accountDefinition.Kind] = count;
                continue;
            }
            var placement = await placements.PlacementForAsync(reward.ItemId, ct).ConfigureAwait(false);
            if (placement is not { WorldClass: ItemWorldClass.Interchangeable, ItemKind: > 0 })
                return EquipmentReinforcementRejection.UnsupportedOutcome;
            var candidates = Enumerable.Range(placement.Destination.FirstIndex,
                    placement.Destination.LastIndex - placement.Destination.FirstIndex + 1)
                .Select(index => new BusinessSlot(placement.Destination.Space, index));
            if (ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind))
                candidates = candidates.Concat(Enumerable.Range(0, 6)
                    .Select(index => new BusinessSlot(BusinessInventorySpace.QuickSlot, index)));
            var available = candidates.ToArray();
            foreach (var space in available.Select(slot => slot.Space).Distinct())
                if (!await LoadAsync(space).ConfigureAwait(false)) return EquipmentReinforcementRejection.NoRoom;
            var remaining = reward.Count;
            while (remaining > 0)
            {
                var arrival = arrivals.ForGrant(
                    available.Select(slot => (Slot: slot, Item: standing.GetValueOrDefault(slot)))
                        .Where(candidate => candidate.Item is { IsPile: true } && candidate.Item.ItemId == reward.ItemId)
                        .Select(candidate => new ItemPile(candidate.Slot, candidate.Item!.Count)).ToArray(),
                    remaining, placement, standing.Keys.ToHashSet());
                BusinessSlot slot;
                int added;
                InventoryItemCoreState core;
                switch (arrival)
                {
                    case ItemArrival.OntoExistingPile pile:
                        slot = pile.Slot;
                        added = pile.Added;
                        core = standing[slot].Core with { Value = pile.Total };
                        break;
                    case ItemArrival.IntoEmptySlot empty:
                        slot = empty.Slot;
                        added = empty.Count;
                        core = StackableItemCore.Create(placement.ItemKind, reward.ItemId, added);
                        break;
                    default:
                        return EquipmentReinforcementRejection.NoRoom;
                }
                if (added <= 0 || added > remaining || ++plannedSlots > byte.MaxValue)
                    return EquipmentReinforcementRejection.NoRoom;
                standing[slot] = new(slot.Index, core, ItemWorldClass.Interchangeable);
                remaining -= added;
            }
        }
        return EquipmentReinforcementRejection.None;
    }
}
