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
    IEquipmentEnchantContentSource content,
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
        var target = await targetBound.Container.ReadSlotAsync(targetBound.Slot.Index, ct)
            .ConfigureAwait(false);
        if (target is null || target.WorldClass != ItemWorldClass.Equipment)
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
        // A21 storage uses EnchantUpgradeCount for normal reinforcement. An upgraded bead's
        // independent enchant level cannot be represented safely in this equipment record.
        if (bead.Core.EnchantUpgradeCount != 0)
            return new(EquipmentEnchantRejection.UnsupportedContent);
        var definition = await content.ResolveAsync(bead.ItemId, target.ItemId, ct)
            .ConfigureAwait(false);
        if (definition is null || definition.BeadItemId != bead.ItemId
            || definition.CardItemId <= 0)
            return new(EquipmentEnchantRejection.UnsupportedContent);

        var consumed = await materials.ConsumeAsync(session,
            [new(new MaterialLocation.Carried(beadSlot), bead.ItemId, 1)], ct)
            .ConfigureAwait(false);
        if (!consumed.Success)
            return new(EquipmentEnchantRejection.MaterialUnavailable);
        var changed = target.Core with { EnchantCardId = definition.CardItemId };
        await targetBound.Container.PutAsync(targetBound.Slot.Index, changed, ct)
            .ConfigureAwait(false);
        return new(EquipmentEnchantRejection.None,
            new(session, beadSlot, targetSlot, definition.CardItemId, changed,
                consumed.Debits[0].Remaining, clock.GetUtcNow()));
    }
}
