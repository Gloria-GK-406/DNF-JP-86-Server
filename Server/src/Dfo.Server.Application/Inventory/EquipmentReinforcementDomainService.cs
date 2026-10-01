using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
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
    IEquipmentEffectPlanRuleService plans,
    IApplyEffectsDomainService effects,
    IInventoryEffectPreflightDomainService preflight,
    ICharacterDomainService characters,
    IAccountMaterialBagDomainService accountMaterials,
    IAccountMaterialRuleService accountMaterialRules,
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
        var plan = await plans.ReinforcementAsync(target, rule,
            new(materialLocation, rule.MaterialItemId, rule.MaterialCount),
            protectionSlot is { } ticket ? new(new MaterialLocation.Carried(ticket), protectionItem!.ItemId, 1) : null,
            new(protectionDefinition, succeeded ? 0 : rolls.Next(10_000), succeeded ? 0 : rolls.Next(10_000), kind), succeeded, ct).ConfigureAwait(false);
        var decision = plan.Decision;
        if (decision.Rejection != EquipmentReinforcementRejection.None) return new(decision.Rejection);
        var context = new EffectExecutionContext(session.AccountId, session.CharacterId, timeProvider.GetUtcNow(), session);
        var preview = await preflight.PreviewAsync(context, plan.Effects, ct).ConfigureAwait(false);
        if (preview.Rejection != InventoryEffectRejection.None)
            return new(preview.Rejection switch
            {
                InventoryEffectRejection.NoRoom => EquipmentReinforcementRejection.NoRoom,
                InventoryEffectRejection.UnsupportedItem => EquipmentReinforcementRejection.UnsupportedOutcome,
                _ => EquipmentReinforcementRejection.InsufficientMaterial,
            });
        if (preview.PhysicalRewardEntries + plan.Effects.Count(effect => effect is GrantAccountMaterialEffect) > byte.MaxValue)
            return new(EquipmentReinforcementRejection.NoRoom);
        var applied = await effects.ApplyAsync(context, plan.Effects, ct).ConfigureAwait(false);
        var final = await EquipmentEffectReceiptProjection.FinalSlotsAsync(session.CharacterId, applied, slots, ct).ConfigureAwait(false);
        var changes = final.Select(change => new EquipmentReinforcementSlotChange(change.Slot, change.Core)).ToArray();
        var accountChanges = EquipmentEffectReceiptProjection.AccountChanges(applied);
        var rewards = applied.Applied.SelectMany(value => value switch
        {
            { Effect: GrantAccountMaterialEffect, Change: AccountMaterialEffectChange account } =>
                [new EquipmentReinforcementReward(null, account.Change.ItemId, account.Change.AddedCount, account.Change.VirtualSlotIndex)],
            { Change: StackableGrantEffectChange physical } => physical.Additions.Select(reward =>
                new EquipmentReinforcementReward(reward.Slot, reward.ItemId, reward.Count)),
            _ => Enumerable.Empty<EquipmentReinforcementReward>(),
        }).ToArray();
        var materialRemaining = materialLocation switch
        {
            MaterialLocation.Account account => (await accountMaterials.Get(session.AccountId).SnapshotAsync(ct).ConfigureAwait(false)).Count(account.Kind),
            MaterialLocation.Carried carried => final.First(change => change.Slot == carried.Slot).Core is { } remaining
                && remaining.ItemId == rule.MaterialItemId ? remaining.Value : 0,
            _ => throw new ArgumentOutOfRangeException(nameof(materialLocation)),
        };
        var receipt = new EquipmentReinforcementReceipt(session, targetSlot, materialSlot,
            decision.ConsumeProtection ? protectionSlot : null, oldLevel, decision.NewLevel,
            decision.Outcome, materialRemaining, applied.Changes<GoldEffectChange>().Single().Remaining,
            changes, Array.AsReadOnly(rewards), accountChanges, context.At);
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

}
