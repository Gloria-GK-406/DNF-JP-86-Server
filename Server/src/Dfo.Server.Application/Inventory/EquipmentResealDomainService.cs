using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentResealDomainService(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers, IBusinessInventorySlotDomainService slots,
    IEquipmentDomainService equipment, IEquipmentResealRuleService rules, IItemLifecycleDomainService lifecycle,
    IDomainTransactionAccessor transactions, TimeProvider clock) : IEquipmentResealDomainService
{
    public async ValueTask<EquipmentQualityResult?> TryResealAsync(SelectedSessionIdentity session,
        BusinessSlot targetSlot, int expectedItemId, BusinessSlot materialSlot, CancellationToken ct)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Equipment resealing requires a transaction.");
        if (sessions.Find(session.AccountId) != session || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive } || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId) return new(EquipmentQualityRejection.NoSession);
        if (targetSlot.Space != BusinessInventorySpace.CarriedEquipment || expectedItemId <= 0
            || materialSlot.Space is not (BusinessInventorySpace.QuickSlot or BusinessInventorySpace.Consumable))
            return new(EquipmentQualityRejection.InvalidRequest);
        var material = slots.Find(session.CharacterId, materialSlot);
        var wax = material is null ? null : await material.Container.GetAsync(materialSlot.Index, ct).ConfigureAwait(false);
        if (wax is null || !rules.SupportsMaterial(wax.ItemId)) return null;
        var now = clock.GetUtcNow();
        if (!wax.IsPile || wax.Count <= 0 || Locked(wax.Snapshot) || Expired(wax.Snapshot))
            return new(EquipmentQualityRejection.MaterialUnavailable);
        var instance = await equipment.Of(session.CharacterId).FindAtAsync(targetSlot, ct).ConfigureAwait(false);
        var target = instance is null ? null : await instance.ReadAsync(ct).ConfigureAwait(false);
        if (target is null || target.ItemId != expectedItemId || target.Snapshot.IsMirror || Expired(target.Snapshot))
            return new(EquipmentQualityRejection.ItemUnavailable);
        if (Locked(target.Snapshot)) return new(EquipmentQualityRejection.Locked);
        var quote = await rules.QuoteAsync(target.Snapshot, ct).ConfigureAwait(false);
        if (quote.Rejection != EquipmentQualityRejection.None) return new(quote.Rejection);
        if (wax.Count < quote.Cost) return new(EquipmentQualityRejection.MaterialUnavailable);
        var remaining = await ItemContainerOperations.ConsumeAsync(material!.Container, wax, quote.Cost, lifecycle, ct).ConfigureAwait(false);
        var changed = await instance!.ResealAsync(quote.NextCount, ct).ConfigureAwait(false);
        return new(EquipmentQualityRejection.None, new(session, targetSlot, materialSlot, changed.Snapshot, remaining, now)
            { Material = wax.Snapshot with { Count = remaining }, Operation = 1 });
        bool Expired(ItemSnapshot item) => item.ExpireTime > 0 && item.ExpireTime <= now.ToUnixTimeSeconds();
        static bool Locked(ItemSnapshot item) => item.EquipmentLockId != 0 || item.SortLockFlag != 0;
    }
}
