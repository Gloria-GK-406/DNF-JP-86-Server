using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentQualityDomainService(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers, IBusinessInventorySlotDomainService slots,
    IEquipmentQualityRuleService rules, IEquipmentQualitySeedSource seeds, IItemLifecycleDomainService lifecycle,
    IDomainTransactionAccessor transactions, TimeProvider clock) : IEquipmentQualityDomainService
{
    public async ValueTask<EquipmentQualityResult> RerollAsync(SelectedSessionIdentity session,
        BusinessSlot targetSlot, int expectedItemId, BusinessSlot materialSlot, CancellationToken ct)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Quality reroll requires a transaction.");
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentQualityRejection.NoSession);
        if (targetSlot.Space != BusinessInventorySpace.CarriedEquipment || expectedItemId <= 0
            || materialSlot.Space is not (BusinessInventorySpace.QuickSlot or BusinessInventorySpace.Consumable)
            || targetSlot == materialSlot)
            return new(EquipmentQualityRejection.InvalidRequest);
        BoundInventorySlot? target;
        BoundInventorySlot? material;
        try { target = slots.Find(session.CharacterId, targetSlot); material = slots.Find(session.CharacterId, materialSlot); }
        catch (InventorySlotMappingException) { return new(EquipmentQualityRejection.InvalidRequest); }
        if (target is null || material is null) return new(EquipmentQualityRejection.ItemUnavailable);
        var item = await target.Container.GetAsync(targetSlot.Index, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (item is null || item.IsPile || item.Snapshot.ItemId != expectedItemId
            || !item.Type.HasFlag(ItemTraits.Equipment) || item.Snapshot.IsMirror || Expired(item.Snapshot))
            return new(EquipmentQualityRejection.ItemUnavailable);
        if (item.Snapshot.EquipmentLockId != 0 || item.Snapshot.SortLockFlag != 0)
            return new(EquipmentQualityRejection.Locked);
        var box = await material.Container.GetAsync(materialSlot.Index, ct).ConfigureAwait(false);
        if (box is not { IsPile: true, Count: > 0 } || Expired(box.Snapshot)
            || box.Snapshot.EquipmentLockId != 0 || box.Snapshot.SortLockFlag != 0)
            return new(EquipmentQualityRejection.MaterialUnavailable);
        var seed = await rules.RollAsync(item.ItemId, box.ItemId, seeds.Next(), ct).ConfigureAwait(false);
        if (seed is null) return new(EquipmentQualityRejection.UnsupportedContent);
        var remaining = await ItemContainerOperations.ConsumeAsync(material.Container, box, 1, lifecycle, ct).ConfigureAwait(false);
        var changed = await target.Container.UpdateAsync(item, current => current with { InstanceValue = seed.Value }, ct).ConfigureAwait(false);
        return new(EquipmentQualityRejection.None, new(session, targetSlot, materialSlot, changed.Snapshot, remaining, now));
        bool Expired(ItemSnapshot snapshot) => snapshot.ExpireTime > 0 && snapshot.ExpireTime <= now.ToUnixTimeSeconds();
    }
}
