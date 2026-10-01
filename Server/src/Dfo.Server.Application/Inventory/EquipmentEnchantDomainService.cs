using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentEnchantDomainService(
    ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers,
    IBusinessInventorySlotDomainService slots,
    IEquipmentDomainService equipments,
    IEquipmentEnchantRuleService rules,
    IMaterialConsumptionDomainService materials,
    TimeProvider clock) : IEquipmentEnchantDomainService
{
    public async ValueTask<EquipmentEnchantResult> EnchantAsync(
        SelectedSessionIdentity session, BusinessSlot beadSlot, BusinessSlot targetSlot,
        CancellationToken ct)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentEnchantRejection.NoSession);

        if (!beadSlot.Space.IsCarriedItemSpace()
            || !(targetSlot.Space == BusinessInventorySpace.CarriedEquipment
                || targetSlot.Space.IsWornEquipmentSpace())
            || beadSlot == targetSlot)
            return new(EquipmentEnchantRejection.InvalidRequest);

        BoundInventorySlot? beadBound;
        BoundInventorySlot? targetBound;
        try
        {
            beadBound = slots.Find(session.CharacterId, beadSlot);
            targetBound = slots.Find(session.CharacterId, targetSlot);
        }
        catch (InventorySlotMappingException)
        {
            return new(EquipmentEnchantRejection.InvalidRequest);
        }
        if (beadBound is null || targetBound is null)
            return new(EquipmentEnchantRejection.ItemUnavailable);
        var bead = await beadBound.Container.ReadSlotAsync(beadBound.Slot.Index, ct)
            .ConfigureAwait(false);
        var instance = await equipments.Of(session.CharacterId).FindAtAsync(targetSlot, ct).ConfigureAwait(false);
        var target = instance is null ? null : await instance.ReadAsync(ct).ConfigureAwait(false);
        if (target is null)
            return new(EquipmentEnchantRejection.ItemUnavailable);
        if (target.Core.ExpireTime > 0
            && target.Core.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentEnchantRejection.ItemUnavailable);
        if (target.Core.EquipmentLockId != 0 || target.Core.SortLockFlag != 0)
            return new(EquipmentEnchantRejection.Locked);
        if (bead is not { IsPile: true, WorldClass: ItemWorldClass.Interchangeable, Count: > 0 })
            return new(EquipmentEnchantRejection.MaterialUnavailable);
        if (bead.Core.EquipmentLockId != 0 || bead.Core.SortLockFlag != 0
            || bead.Core.ExpireTime > 0
                && bead.Core.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentEnchantRejection.MaterialUnavailable);
        var cardId = await rules.CardAsync(bead.Core, target.Core.ItemId, ct).ConfigureAwait(false);
        if (cardId is null) return new(EquipmentEnchantRejection.UnsupportedContent);

        var consumed = await materials.ConsumeAsync(session,
            [new(new MaterialLocation.Carried(beadSlot), bead.ItemId, 1)], ct)
            .ConfigureAwait(false);
        if (!consumed.Success)
            return new(EquipmentEnchantRejection.MaterialUnavailable);
        var changed = (await instance!.SetEnchantmentAsync(cardId.Value, ct).ConfigureAwait(false)).Core;
        return new(EquipmentEnchantRejection.None,
            new(session, beadSlot, targetSlot, cardId.Value, changed,
                consumed.Debits[0].Remaining, clock.GetUtcNow()));
    }
}
