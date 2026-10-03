using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentForgingDomainService(
    ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers,
    IBusinessInventorySlotDomainService slots,
    IEquipmentDomainService equipments,
    IEquipmentEffectPlanRuleService rules,
    IEquipmentForgingRollSource rolls,
    IApplyEffectsDomainService effects,
    IInventoryEffectPreflightDomainService preflight,
    TimeProvider clock) : IEquipmentForgingDomainService
{
    public async ValueTask<EquipmentForgingResult> ForgeAsync(SelectedSessionIdentity session,
        BusinessSlot targetSlot, int expectedTargetItemId, BusinessSlot materialSlot,
        CancellationToken ct)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentForgingRejection.NoSession);
        if (targetSlot.Space is not (BusinessInventorySpace.CarriedEquipment
                or BusinessInventorySpace.WornEquipment
                or BusinessInventorySpace.WornEquipmentExtension)
            || !materialSlot.Space.IsCarriedItemSpace() || targetSlot == materialSlot
            || expectedTargetItemId <= 0)
            return new(EquipmentForgingRejection.InvalidRequest);

        BoundInventorySlot? targetBound;
        BoundInventorySlot? materialBound;
        try
        {
            targetBound = slots.Find(session.CharacterId, targetSlot);
            materialBound = slots.Find(session.CharacterId, materialSlot);
        }
        catch (InventorySlotMappingException)
        {
            return new(EquipmentForgingRejection.InvalidRequest);
        }
        if (targetBound is null || materialBound is null)
            return new(EquipmentForgingRejection.ItemUnavailable);
        var instance = await equipments.Of(session.CharacterId).FindAtAsync(targetSlot, ct).ConfigureAwait(false);
        var target = instance is null ? null : await instance.ReadAsync(ct).ConfigureAwait(false);
        if (target is null)
            return new(EquipmentForgingRejection.ItemUnavailable);
        if (target.Snapshot.ItemId != expectedTargetItemId)
            return new(EquipmentForgingRejection.WrongItem);
        if (target.Snapshot.EquipmentLockId != 0 || target.Snapshot.SortLockFlag != 0)
            return new(EquipmentForgingRejection.Locked);
        if (target.Snapshot.ExpireTime > 0
            && target.Snapshot.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentForgingRejection.Expired);

        var material = await materialBound.Container.GetAsync(materialBound.Slot.Index, ct)
            .ConfigureAwait(false);
        if (material is not { IsPile: true, Count: > 0 }
            || material.Snapshot.EquipmentLockId != 0 || material.Snapshot.SortLockFlag != 0
            || material.Snapshot.ExpireTime > 0
                && material.Snapshot.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentForgingRejection.MaterialUnavailable);
        var plan = await rules.ForgingAsync(target, materialSlot, material.ItemId, rolls.Next(), ct).ConfigureAwait(false);
        var decision = plan.Decision;
        if (decision.Rejection != EquipmentForgingRejection.None)
            return new(decision.Rejection);
        if (decision.MaterialCount <= 0 || material.Count < decision.MaterialCount)
            return new(EquipmentForgingRejection.MaterialUnavailable);
        var context = new EffectExecutionContext(session.AccountId, session.CharacterId, clock.GetUtcNow(), session);
        if ((await preflight.PreviewAsync(context, plan.Effects, ct).ConfigureAwait(false)).Rejection != InventoryEffectRejection.None)
            return new(EquipmentForgingRejection.MaterialUnavailable);
        var applied = await effects.ApplyAsync(context, plan.Effects, ct).ConfigureAwait(false);
        var changed = decision.Succeeded
            ? applied.Changes<EquipmentEffectChange>().Single().After!
            : target.Snapshot;
        var remaining = await materialBound.Container.GetAsync(materialSlot.Index, ct).ConfigureAwait(false);
        return new(EquipmentForgingRejection.None, new(session, targetSlot, target.Snapshot.ItemId,
            materialSlot, material.ItemId, remaining?.Count ?? 0,
            changed, target.Snapshot.GenuineUpgrade, decision.NewLevel, decision.Succeeded,
            decision.SuccessWeight, decision.Roll, context.At));
    }
}
