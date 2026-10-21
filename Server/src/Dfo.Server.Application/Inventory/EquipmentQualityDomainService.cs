using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentQualityDomainService(ISelectedSessionDomainService sessions,
    IBusinessInventorySlotDomainService slots,
    IEquipmentQualityRuleService rules, IEquipmentMagicSealGradeRuleService magicGrades,
    IEquipmentQualitySeedSource seeds, IItemLifecycleDomainService lifecycle,
    IDomainTransactionAccessor transactions, TimeProvider clock) : IEquipmentQualityDomainService
{
    public ValueTask<EquipmentQualityResult> RerollAsync(SelectedSessionIdentity session,
        BusinessSlot targetSlot, int expectedItemId, BusinessSlot materialSlot, CancellationToken ct)
        => expectedItemId <= 0 ? ValueTask.FromResult(new EquipmentQualityResult(EquipmentQualityRejection.InvalidRequest))
            : RerollCoreAsync(session, targetSlot, expectedItemId, materialSlot, null, ct);

    public ValueTask<EquipmentQualityResult> RerollMagicSealGradeAsync(SelectedSessionIdentity session,
        BusinessSlot targetSlot, BusinessSlot materialSlot, ushort optionIndex, CancellationToken ct)
        => optionIndex > 2 ? ValueTask.FromResult(new EquipmentQualityResult(EquipmentQualityRejection.InvalidOption))
            : RerollCoreAsync(session, targetSlot, null, materialSlot, (byte)optionIndex, ct);

    private async ValueTask<EquipmentQualityResult> RerollCoreAsync(SelectedSessionIdentity session,
        BusinessSlot targetSlot, int? expectedItemId, BusinessSlot materialSlot, byte? optionIndex, CancellationToken ct)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Quality reroll requires a transaction.");
        if (!sessions.IsInWorld(session))
            return new(EquipmentQualityRejection.NoSession);
        if (targetSlot.Space != BusinessInventorySpace.CarriedEquipment
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
        if (item is null || item.IsPile || expectedItemId is { } expected && item.Snapshot.ItemId != expected
            || !item.Type.HasFlag(ItemTraits.Equipment) || item.Snapshot.IsMirror || Expired(item.Snapshot))
            return new(EquipmentQualityRejection.ItemUnavailable);
        if (item.Snapshot.EquipmentLockId != 0 || item.Snapshot.SortLockFlag != 0)
            return new(EquipmentQualityRejection.Locked);
        var box = await material.Container.GetAsync(materialSlot.Index, ct).ConfigureAwait(false);
        if (box is not { IsPile: true, Count: > 0 } || Expired(box.Snapshot)
            || box.Snapshot.EquipmentLockId != 0 || box.Snapshot.SortLockFlag != 0)
            return new(EquipmentQualityRejection.MaterialUnavailable);
        ItemSnapshot next;
        if (optionIndex is { } index)
        {
            var selected = index switch { 0 => item.Snapshot.RandomOption0, 1 => item.Snapshot.RandomOption1, _ => item.Snapshot.RandomOption2 };
            var decision = await magicGrades.RollAsync(item.ItemId, box.ItemId, selected, seeds.Next(), ct).ConfigureAwait(false);
            if (decision.Option is not { } option) return new(decision.Rejection);
            next = index switch
            {
                0 => item.Snapshot with { RandomOption0 = option },
                1 => item.Snapshot with { RandomOption1 = option },
                _ => item.Snapshot with { RandomOption2 = option },
            };
        }
        else
        {
            var seed = await rules.RollAsync(item.ItemId, box.ItemId, seeds.Next(), ct).ConfigureAwait(false);
            if (seed is null) return new(EquipmentQualityRejection.UnsupportedContent);
            next = item.Snapshot with { InstanceValue = seed.Value };
        }
        var remaining = await ItemContainerOperations.ConsumeAsync(material.Container, box, 1, lifecycle, ct).ConfigureAwait(false);
        var changed = await target.Container.UpdateAsync(item, _ => next, ct).ConfigureAwait(false);
        return new(EquipmentQualityRejection.None, new(session, targetSlot, materialSlot, changed.Snapshot, remaining, now)
            { Material = box.Snapshot with { Count = remaining } });
        bool Expired(ItemSnapshot snapshot) => snapshot.ExpireTime > 0 && snapshot.ExpireTime <= now.ToUnixTimeSeconds();
    }
}
