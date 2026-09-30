using Dfo.Server.Abstractions.Content;
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
    IEquipmentForgingRuleService rules,
    IEquipmentForgingRollSource rolls,
    IMaterialConsumptionDomainService materials,
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
        var target = await targetBound.Container.ReadSlotAsync(targetBound.Slot.Index, ct)
            .ConfigureAwait(false);
        if (target is null || target.WorldClass != ItemWorldClass.Equipment)
            return new(EquipmentForgingRejection.ItemUnavailable);
        if (target.ItemId != expectedTargetItemId)
            return new(EquipmentForgingRejection.WrongItem);
        if (target.Core.EquipmentLockId != 0 || target.Core.SortLockFlag != 0)
            return new(EquipmentForgingRejection.Locked);
        if (target.Core.ExpireTime > 0
            && target.Core.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentForgingRejection.Expired);

        var material = await materialBound.Container.ReadSlotAsync(materialBound.Slot.Index, ct)
            .ConfigureAwait(false);
        if (material is not { IsPile: true, WorldClass: ItemWorldClass.Interchangeable, Count: > 0 }
            || material.Core.EquipmentLockId != 0 || material.Core.SortLockFlag != 0
            || material.Core.ExpireTime > 0
                && material.Core.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentForgingRejection.MaterialUnavailable);
        var decision = await rules.DecideAsync(target.ItemId, target.Core.Durability,
            target.Core.GenuineUpgrade, material.ItemId, rolls.Next(), ct)
            .ConfigureAwait(false);
        if (decision.Rejection != EquipmentForgingRejection.None)
            return new(decision.Rejection);
        if (decision.MaterialCount <= 0 || material.Count < decision.MaterialCount)
            return new(EquipmentForgingRejection.MaterialUnavailable);
        var consumed = await materials.ConsumeAsync(session,
            [new(new MaterialLocation.Carried(materialSlot), material.ItemId,
                decision.MaterialCount)], ct).ConfigureAwait(false);
        if (!consumed.Success) return new(EquipmentForgingRejection.MaterialUnavailable);
        var changed = decision.Succeeded
            ? target.Core with { GenuineUpgrade = decision.NewLevel }
            : target.Core;
        if (decision.Succeeded)
            await targetBound.Container.PutAsync(targetBound.Slot.Index, changed, ct)
                .ConfigureAwait(false);
        return new(EquipmentForgingRejection.None, new(session, targetSlot, target.ItemId,
            materialSlot, material.ItemId, consumed.Debits[0].Remaining?.Value ?? 0,
            changed, target.Core.GenuineUpgrade, decision.NewLevel, decision.Succeeded,
            decision.SuccessWeight, decision.Roll, clock.GetUtcNow()));
    }
}
