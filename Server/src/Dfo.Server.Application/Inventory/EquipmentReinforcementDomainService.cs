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
    IEquipmentReinforcementDefinitionSource definitions,
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
        CancellationToken ct)
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

        var targetBound = slots.Find(session.CharacterId, targetSlot);
        var target = targetBound is null ? null
            : await targetBound.Container.ReadSlotAsync(targetBound.Slot.Index, ct).ConfigureAwait(false);
        if (target is null || target.WorldClass != ItemWorldClass.Equipment)
            return new(EquipmentReinforcementRejection.ItemUnavailable);
        if (target.ItemId != expectedItemId)
            return new(EquipmentReinforcementRejection.WrongItem);
        if (target.Core.EquipmentLockId != 0 || target.Core.SortLockFlag != 0)
            return new(EquipmentReinforcementRejection.Locked);
        if (target.Core.SealFlag != 0 || target.Core.AmplifyType != 0)
            return new(EquipmentReinforcementRejection.CannotReinforce);

        var equipment = await definitions.GetEquipmentAsync(target.ItemId, ct).ConfigureAwait(false);
        var table = await definitions.GetNormalTableAsync(ct).ConfigureAwait(false);
        if (equipment is not { CanReinforce: true } || table is null)
            return new(EquipmentReinforcementRejection.UnsupportedContent);
        if (target.Core.Durability != equipment.MaxDurability)
            return new(EquipmentReinforcementRejection.Durability);

        var oldLevel = target.Core.EnchantUpgradeCount;
        // A21 upstream rejects current levels above 30 even though the archive carries many
        // rows for other table uses. Keep that admission ceiling until a client trace says more.
        if (oldLevel > 30
            || table.MaxUpgradeLevelByRarity.TryGetValue(equipment.Rarity, out var maximum)
                && oldLevel >= maximum)
            return new(EquipmentReinforcementRejection.CannotReinforce);
        var row = table.Rows.FirstOrDefault(candidate => candidate.TargetLevel == oldLevel + 1);
        if (row is null || row.FailureThreshold is < 0 or > 100_000
            || row.MaterialItemId <= 0 || row.MaterialCount <= 0
            || row.PenaltyType is < 1 or > 3 || row.PenaltyValue < 0
            || !TryGoldCost(table, equipment, oldLevel, out var goldCost))
            return new(EquipmentReinforcementRejection.UnsupportedContent);

        var accountKind = accountMaterialRules.TryGetByItemId(row.MaterialItemId, out var accountDefinition)
            ? accountDefinition.Kind : (AccountMaterialKind?)null;
        MaterialLocation materialLocation;
        if (accountMaterialItemId is { } requestedAccountMaterial)
        {
            if (requestedAccountMaterial != row.MaterialItemId || accountKind is not { } kind)
                return new(EquipmentReinforcementRejection.InsufficientMaterial);
            var accountCount = (await accountMaterials.Get(session.AccountId)
                .SnapshotAsync(ct).ConfigureAwait(false)).Count(kind);
            if (accountCount < row.MaterialCount)
                return new(EquipmentReinforcementRejection.InsufficientMaterial);
            materialLocation = new MaterialLocation.Account(kind);
        }
        else if (materialSlot is { } sourceSlot)
        {
            var materialBound = slots.Find(session.CharacterId, sourceSlot);
            var material = materialBound is null ? null
                : await materialBound.Container.ReadSlotAsync(materialBound.Slot.Index, ct).ConfigureAwait(false);
            if (material is not { IsPile: true } || material.ItemId != row.MaterialItemId
                || material.Count < row.MaterialCount)
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
            protectionDefinition = await definitions.GetProtectionAsync(protectionItem.ItemId, ct)
                .ConfigureAwait(false);
            if (protectionDefinition is null)
                return new(EquipmentReinforcementRejection.InvalidRequest);
        }

        var character = characters.Get(session.CharacterId);
        if (await character.IdentityAsync(ct).ConfigureAwait(false) is null)
            return new(EquipmentReinforcementRejection.NoSession);
        if (await character.GoldAsync(ct).ConfigureAwait(false) < goldCost)
            return new(EquipmentReinforcementRejection.InsufficientGold);

        var penalty = table.DestroyLevelByRarity.TryGetValue(equipment.Rarity, out var destroyLevel)
            && oldLevel >= destroyLevel ? 3 : row.PenaltyType;
        if (penalty == 3 && protectionSlot is null)
        {
            var automatic = await FindAutomaticProtectionAsync(session, oldLevel, ct)
                .ConfigureAwait(false);
            if (automatic is { } selected)
            {
                protectionSlot = selected.Slot;
                protectionItem = selected.Item;
                protectionDefinition = selected.Definition;
            }
        }
        int? retainLevel = penalty == 3 && protectionDefinition is { } ticket
            && (ticket.Kind == EquipmentReinforcementProtectionKind.ResetToZero
                || oldLevel > ticket.RetainedLevel)
                ? ticket.RetainedLevel : null;
        var decision = EquipmentReinforcementRules.Decide(
            new(oldLevel, row.FailureThreshold, penalty, row.PenaltyValue, retainLevel),
            rolls.Next(100_000));
        int destroyBonusCount = 0;
        if (decision.Outcome == EquipmentReinforcementOutcome.Destroyed)
        {
            if (!TryDestroyBonus(table.DestroyBonus, equipment, oldLevel + 1,
                    out destroyBonusCount))
                return new(EquipmentReinforcementRejection.UnsupportedOutcome);
            if (destroyBonusCount > 0 && !await CanPlaceDestroyBonusAsync(
                    session, table.DestroyBonus.ItemId, destroyBonusCount,
                    materialLocation, row.MaterialCount, protectionSlot,
                    decision.ConsumeProtection, ct).ConfigureAwait(false))
                return new(EquipmentReinforcementRejection.NoRoom);
        }

        var costs = new List<MaterialCost>
        {
            new(materialLocation, row.MaterialItemId, row.MaterialCount),
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
            await targetBound!.Container.ClearAsync(targetBound.Slot.Index, ct).ConfigureAwait(false);
        else
        {
            updated = target.Core with { EnchantUpgradeCount = decision.NewLevel };
            await targetBound!.Container.PutAsync(targetBound.Slot.Index, updated, ct)
                .ConfigureAwait(false);
        }
        var changes = new List<EquipmentReinforcementSlotChange> { new(targetSlot, updated) };
        foreach (var debit in debited.Debits)
            if (debit.Cost.Location is MaterialLocation.Carried carried)
                changes.Add(new(carried.Slot, debit.Remaining));
        var accountChanges = debited.Debits
            .Select(debit => debit.AccountChange).OfType<AccountMaterialChange>().ToArray();
        var rewards = new List<EquipmentReinforcementReward>();
        if (destroyBonusCount > 0)
        {
            // CharacterContainerAddition performs whole-quantity staging under this command's
            // ambient transaction. Any unexpected placement failure throws and rolls it back.
            var addition = await containers.AddAsync(session.CharacterId,
                    table.DestroyBonus.ItemId, destroyBonusCount, ct).ConfigureAwait(false);
            foreach (var item in addition.Items)
            {
                rewards.Add(new(item.Slot, table.DestroyBonus.ItemId, item.Count));
                var bound = slots.Get(session.CharacterId, item.Slot);
                var final = await bound.Container.ReadSlotAsync(bound.Slot.Index, ct)
                    .ConfigureAwait(false);
                changes.Add(new(item.Slot, final?.Core));
            }
            if (addition.AccountMaterial is { } addedAccount)
                accountChanges = [.. accountChanges, addedAccount];
        }
        var materialRemaining = debited.Debits[0] is { AccountChange: { } accountChange }
            ? accountChange.FinalCount : debited.Debits[0].Remaining?.Value ?? 0;
        var receipt = new EquipmentReinforcementReceipt(session, targetSlot, materialSlot,
            decision.ConsumeProtection ? protectionSlot : null, oldLevel, decision.NewLevel,
            decision.Outcome, materialRemaining, gold, changes, rewards, accountChanges,
            timeProvider.GetUtcNow());
        return new(EquipmentReinforcementRejection.None, receipt);
    }

    private static bool TryDestroyBonus(EquipmentReinforcementDestroyBonusDefinition bonus,
        EquipmentReinforcementEquipmentDefinition equipment, int targetLevel, out int count)
    {
        count = 0;
        if (bonus.ItemId <= 0 || bonus.MinimumCorrectedGrade < 0 || bonus.UpgradeLevelBase < 0)
            return false;
        if (targetLevel <= bonus.UpgradeLevelBase
            || !bonus.WeightByRarity.TryGetValue(equipment.Rarity, out var weight))
            return true;
        var correction = bonus.GradeCorrectionByRarity.GetValueOrDefault(equipment.Rarity);
        if (Math.Max(0, equipment.Grade - correction) < bonus.MinimumCorrectedGrade)
            return true;
        var distance = (double)targetLevel - bonus.UpgradeLevelBase;
        var raw = distance * distance * weight;
        if (!double.IsFinite(raw) || raw < 0 || raw > int.MaxValue)
            return false;
        count = (int)raw;
        return true;
    }

    private async ValueTask<(BusinessSlot Slot, ContainerSlotItem Item,
        EquipmentReinforcementProtectionDefinition Definition)?> FindAutomaticProtectionAsync(
        SelectedSessionIdentity session, byte oldLevel, CancellationToken ct)
    {
        var choices = new List<(BusinessSlot Slot, ContainerSlotItem Item,
            EquipmentReinforcementProtectionDefinition Definition)>();
        foreach (var space in new[] { BusinessInventorySpace.QuickSlot,
                     BusinessInventorySpace.Material })
        {
            var bound = slots.Find(session.CharacterId, new(space, 0));
            if (bound is null) continue;
            foreach (var item in await bound.Container.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (!item.IsPile || item.Count <= 0
                    || item.Core.ExpireTime > 0
                        && item.Core.ExpireTime <= timeProvider.GetUtcNow().ToUnixTimeSeconds())
                    continue;
                var definition = await definitions.GetProtectionAsync(item.ItemId, ct)
                    .ConfigureAwait(false);
                if (definition is null
                    || definition.Kind == EquipmentReinforcementProtectionKind.RetainLevel
                        && oldLevel <= definition.RetainedLevel)
                    continue;
                choices.Add((new(space, item.SlotIndex), item, definition));
            }
        }
        // The A21 item script for the super ticket explicitly says it takes precedence over
        // ordinary protection. Within one kind, keep the inventory order deterministic.
        return choices.OrderByDescending(choice =>
                choice.Definition.Kind == EquipmentReinforcementProtectionKind.RetainLevel)
            .ThenBy(choice => choice.Slot.Space)
            .ThenBy(choice => choice.Slot.Index)
            .Cast<(BusinessSlot Slot, ContainerSlotItem Item,
                EquipmentReinforcementProtectionDefinition Definition)?>()
            .FirstOrDefault();
    }

    private async ValueTask<bool> CanPlaceDestroyBonusAsync(SelectedSessionIdentity session,
        int itemId, int count, MaterialLocation materialLocation, int materialCount,
        BusinessSlot? protectionSlot, bool consumeProtection, CancellationToken ct)
    {
        var placement = await placements.PlacementForAsync(itemId, ct).ConfigureAwait(false);
        if (placement is not { WorldClass: ItemWorldClass.Interchangeable, ItemKind: > 0 })
            return false;
        var spaces = new[] { placement.Destination.Space,
            BusinessInventorySpace.QuickSlot }.Distinct().ToArray();
        var standing = new Dictionary<BusinessSlot, ContainerSlotItem>();
        foreach (var space in spaces)
        {
            var bound = slots.Find(session.CharacterId, new(space, 0));
            if (bound is null) return false;
            foreach (var item in await bound.Container.ReadAllAsync(ct).ConfigureAwait(false))
                standing[new(space, item.SlotIndex)] = item;
        }
        if (materialLocation is MaterialLocation.Carried carried
            && standing.TryGetValue(carried.Slot, out var source)
            && source.Count == materialCount)
            standing.Remove(carried.Slot);
        if (consumeProtection && protectionSlot is { } ticket
            && standing.TryGetValue(ticket, out var protector) && protector.Count == 1)
            standing.Remove(ticket);
        var remaining = count;
        var plannedSlots = 0;
        while (remaining > 0)
        {
            var candidates = Enumerable.Range(placement.Destination.FirstIndex,
                    placement.Destination.LastIndex - placement.Destination.FirstIndex + 1)
                .Select(index => new BusinessSlot(placement.Destination.Space, index));
            if (ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind))
                candidates = candidates.Concat(Enumerable.Range(0, 6)
                    .Select(index => new BusinessSlot(BusinessInventorySpace.QuickSlot, index)));
            var arrival = arrivals.ForGrant(
                candidates.Select(slot => (Slot: slot, Item: standing.GetValueOrDefault(slot)))
                    .Where(candidate => candidate.Item is { IsPile: true }
                        && candidate.Item.ItemId == itemId)
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
                    core = StackableItemCore.Create(placement.ItemKind, itemId, added);
                    break;
                default:
                    return false;
            }
            if (added <= 0 || added > remaining || ++plannedSlots > byte.MaxValue)
                return false;
            standing[slot] = new(slot.Index, core, ItemWorldClass.Interchangeable);
            remaining -= added;
        }
        return true;
    }

    private static bool TryGoldCost(EquipmentReinforcementTable table,
        EquipmentReinforcementEquipmentDefinition equipment, byte oldLevel, out int cost)
    {
        cost = 0;
        var levelIndex = equipment.MinimumLevel + Math.Max(0, equipment.Rarity - 1) * 2;
        if (levelIndex < 0 || levelIndex >= table.BaseGoldByEquipmentLevel.Count
            || equipment.Rarity < 0 || equipment.Rarity >= table.CostWeightsByRarity.Count
            || equipment.EquipmentTypeWeightIndex < 0
            || equipment.EquipmentTypeWeightIndex >= table.CostWeightsByEquipmentType.Count)
            return false;
        // A21's normal table continues beyond the last explicit cost-weight entry.
        // The upstream provider treats a missing level weight as the neutral factor.
        var levelWeight = table.CostWeightByCurrentLevel.GetValueOrDefault(oldLevel, 1);
        if (!double.IsFinite(levelWeight) || levelWeight <= 0)
            return false;
        var raw = (double)table.BaseGoldByEquipmentLevel[levelIndex]
            * table.CostWeightsByRarity[equipment.Rarity]
            * table.CostWeightsByEquipmentType[equipment.EquipmentTypeWeightIndex]
            * levelWeight;
        if (!double.IsFinite(raw) || raw < 0 || raw > int.MaxValue)
            return false;
        cost = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        return true;
    }
}
