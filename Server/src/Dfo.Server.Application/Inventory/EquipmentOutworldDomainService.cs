using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentOutworldDomainService(
    ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers,
    IBusinessInventorySlotDomainService slots,
    IEquipmentDomainService equipments,
    IEquipmentEffectPlanRuleService rules,
    IEquipmentOutworldSeedSource seeds,
    IApplyEffectsDomainService effects,
    IInventoryEffectPreflightDomainService preflight,
    TimeProvider clock) : IEquipmentOutworldDomainService
{
    public async ValueTask<EquipmentOutworldResult> ApplyAsync(
        SelectedSessionIdentity session, EquipmentOutworldAction action,
        BusinessSlot targetSlot, int expectedTargetItemId,
        BusinessSlot materialSlot, int expectedMaterialItemId,
        byte selectedOption, CancellationToken ct)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentOutworldRejection.NoSession);
        if (targetSlot.Space != BusinessInventorySpace.CarriedEquipment
            || !materialSlot.Space.IsCarriedItemSpace() || targetSlot == materialSlot
            || expectedTargetItemId <= 0 || expectedMaterialItemId <= 0)
            return new(EquipmentOutworldRejection.InvalidRequest);

        BoundInventorySlot? targetBound;
        BoundInventorySlot? materialBound;
        try
        {
            targetBound = slots.Find(session.CharacterId, targetSlot);
            materialBound = slots.Find(session.CharacterId, materialSlot);
        }
        catch (InventorySlotMappingException)
        {
            return new(EquipmentOutworldRejection.InvalidRequest);
        }
        if (targetBound is null || materialBound is null)
            return new(EquipmentOutworldRejection.ItemUnavailable);
        var instance = await equipments.Of(session.CharacterId).FindAtAsync(targetSlot, ct).ConfigureAwait(false);
        var target = instance is null ? null : await instance.ReadAsync(ct).ConfigureAwait(false);
        if (target is null)
            return new(EquipmentOutworldRejection.ItemUnavailable);
        if (target.Snapshot.ItemId != expectedTargetItemId)
            return new(EquipmentOutworldRejection.WrongItem);
        if (target.Snapshot.ExpireTime > 0
            && target.Snapshot.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentOutworldRejection.ItemUnavailable);
        if (target.Snapshot.EquipmentLockId != 0 || target.Snapshot.SortLockFlag != 0)
            return new(EquipmentOutworldRejection.Locked);
        var material = await materialBound.Container.GetAsync(materialBound.Slot.Index, ct)
            .ConfigureAwait(false);
        if (material is not { IsPile: true, Count: > 0 }
            || material.ItemId != expectedMaterialItemId
            || material.Snapshot.EquipmentLockId != 0 || material.Snapshot.SortLockFlag != 0
            || material.Snapshot.ExpireTime > 0
                && material.Snapshot.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentOutworldRejection.MaterialUnavailable);

        var plan = await rules.OutworldAsync(target, action, materialSlot, material.ItemId, selectedOption, seeds.NextSeed(), ct)
            .ConfigureAwait(false);
        var decision = plan.Decision;
        if (decision.Rejection != EquipmentOutworldRejection.None)
            return new(decision.Rejection);
        if (decision.MaterialCount <= 0 || material.Count < decision.MaterialCount)
            return new(EquipmentOutworldRejection.MaterialUnavailable);
        var context = new EffectExecutionContext(session.AccountId, session.CharacterId, clock.GetUtcNow(), session);
        if ((await preflight.PreviewAsync(context, plan.Effects, ct).ConfigureAwait(false)).Rejection != InventoryEffectRejection.None)
            return new(EquipmentOutworldRejection.MaterialUnavailable);
        var applied = await effects.ApplyAsync(context, plan.Effects, ct).ConfigureAwait(false);
        var changed = applied.Changes<EquipmentEffectChange>().Single().After!;
        var remaining = await materialBound.Container.GetAsync(materialSlot.Index, ct).ConfigureAwait(false);
        return new(EquipmentOutworldRejection.None,
            new(session, action, targetSlot, target.Snapshot.ItemId, materialSlot, material.ItemId,
                remaining?.Count ?? 0, changed,
                decision.Cleared, decision.Seed, context.At));
    }
}
