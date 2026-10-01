using Dfo.Server.Abstractions.Content;
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
    IEquipmentUpgradeRuleService rules,
    IEquipmentOutworldSeedSource seeds,
    IMaterialConsumptionDomainService materials,
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
        if (target.Core.ItemId != expectedTargetItemId)
            return new(EquipmentOutworldRejection.WrongItem);
        if (target.Core.ExpireTime > 0
            && target.Core.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentOutworldRejection.ItemUnavailable);
        if (target.Core.EquipmentLockId != 0 || target.Core.SortLockFlag != 0)
            return new(EquipmentOutworldRejection.Locked);
        var material = await materialBound.Container.ReadSlotAsync(materialBound.Slot.Index, ct)
            .ConfigureAwait(false);
        if (material is not { IsPile: true, WorldClass: ItemWorldClass.Interchangeable, Count: > 0 }
            || material.ItemId != expectedMaterialItemId
            || material.Core.EquipmentLockId != 0 || material.Core.SortLockFlag != 0
            || material.Core.ExpireTime > 0
                && material.Core.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentOutworldRejection.MaterialUnavailable);

        var decision = await rules.DecideOutworldAsync(action, target.Core.ItemId, target.Core.AmplifyType,
            target.Core.ReinforcementLevel, material.ItemId, selectedOption, seeds.NextSeed(), ct)
            .ConfigureAwait(false);
        if (decision.Rejection != EquipmentOutworldRejection.None)
            return new(decision.Rejection);
        if (decision.MaterialCount <= 0 || material.Count < decision.MaterialCount)
            return new(EquipmentOutworldRejection.MaterialUnavailable);
        var consumed = await materials.ConsumeAsync(session,
            [new(new MaterialLocation.Carried(materialSlot), material.ItemId, decision.MaterialCount)], ct)
            .ConfigureAwait(false);
        if (!consumed.Success)
            return new(EquipmentOutworldRejection.MaterialUnavailable);
        var changed = (await instance!.SetDimensionalAttributeAsync(decision.AmplifyType,
            decision.AmplifyValue, ct).ConfigureAwait(false)).Core;
        return new(EquipmentOutworldRejection.None,
            new(session, action, targetSlot, target.Core.ItemId, materialSlot, material.ItemId,
                consumed.Debits[0].Remaining?.Value ?? 0, changed,
                decision.Cleared, decision.Seed, clock.GetUtcNow()));
    }
}
