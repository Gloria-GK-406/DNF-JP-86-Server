using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class InventorySortDomainService(
    ICharacterBusinessInventoryDomainService inventories, IInventorySortRuleService sorting)
    : IInventorySortDomainService
{
    public async ValueTask<bool?> ChangeLockAsync(CharacterId character, BusinessSlot slot,
        InventorySortLockAction action, CancellationToken ct)
    {
        if (!Enum.IsDefined(action) || !sorting.IsSortable(slot.Space)
            || slot.Space == BusinessInventorySpace.AvatarEmblem || slot.Index < 0) return null;
        var container = inventories.GetStore(character, BusinessInventoryOwnership.StoreOf(slot.Space))
            .FindContainer(slot.Space);
        if (container is null || slot.Index >= await container.OpenSlotCountAsync(ct).ConfigureAwait(false)) return null;
        var item = await container.GetAsync(slot.Index, ct).ConfigureAwait(false);
        if (item is null) return null;
        var locked = action == InventorySortLockAction.Toggle && item.Snapshot.SortLockFlag != 1;
        var flag = locked ? (byte)1 : (byte)0;
        if (item.Snapshot.SortLockFlag != flag)
            await container.UpdateAsync(item, current => current with { SortLockFlag = flag }, ct).ConfigureAwait(false);
        return locked;
    }

    public async ValueTask<bool> SortAsync(CharacterId character, BusinessInventorySpace space,
        InventorySortOrder order, CancellationToken ct)
    {
        if (!Enum.IsDefined(order) || !sorting.IsSortable(space)) return false;
        var container = inventories.GetStore(character, BusinessInventoryOwnership.StoreOf(space)).GetContainer(space);
        var open = await container.OpenSlotCountAsync(ct).ConfigureAwait(false);
        if (open <= 0) return false;
        var items = (await container.GetAllAsync(ct).ConfigureAwait(false)).Where(i => i.SlotIndex < open).ToArray();
        var locked = items.Where(item => item.Snapshot.SortLockFlag == 1).Select(item => item.SlotIndex).ToHashSet();
        var movable = items.Where(item => !locked.Contains(item.SlotIndex)).OrderBy(item => item.SlotIndex).ToArray();
        var ordered = await sorting.OrderAsync(space, order, movable, ct).ConfigureAwait(false);
        var target = 0;
        var plan = new List<(ItemReference Item, int Target)>();
        foreach (var item in ordered)
        {
            while (locked.Contains(target)) target++;
            plan.Add((item, target++));
        }
        if (plan.All(move => move.Item.SlotIndex == move.Target)) return true;
        var records = new List<(ItemRecord Record, int Target)>();
        foreach (var move in plan)
            records.Add((await container.TakeAsync(move.Item, null, ct).ConfigureAwait(false), move.Target));
        foreach (var move in records)
            await container.PutAsync(move.Target, move.Record, ct).ConfigureAwait(false);
        await container.FlushAsync(ct).ConfigureAwait(false);
        return true;
    }
}
