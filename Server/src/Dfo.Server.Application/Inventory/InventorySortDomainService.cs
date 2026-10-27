using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class InventorySortDomainService(
    ICharacterBusinessInventoryDomainService inventories, IInventorySortKeySource keys)
    : IInventorySortDomainService
{
    public async ValueTask<bool?> ChangeLockAsync(CharacterId character, BusinessSlot slot,
        InventorySortLockAction action, CancellationToken ct)
    {
        if (!Enum.IsDefined(action) || !IsSortable(slot.Space)
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

    private static bool IsSortable(BusinessInventorySpace space) => space is
        BusinessInventorySpace.CarriedEquipment or BusinessInventorySpace.Consumable
        or BusinessInventorySpace.Material or BusinessInventorySpace.Quest or BusinessInventorySpace.ExpertMaterial
        or BusinessInventorySpace.AvatarEmblem or BusinessInventorySpace.Avatar
        or BusinessInventorySpace.CreatureEquipment or BusinessInventorySpace.CreatureConsumable
        or BusinessInventorySpace.PersonalCargo or BusinessInventorySpace.AccountCargo;

    public async ValueTask<bool> SortAsync(CharacterId character, BusinessInventorySpace space,
        InventorySortOrder order, CancellationToken ct)
    {
        if (!Enum.IsDefined(order) || !IsSortable(space)) return false;
        var container = inventories.GetStore(character, BusinessInventoryOwnership.StoreOf(space)).GetContainer(space);
        var open = await container.OpenSlotCountAsync(ct).ConfigureAwait(false);
        if (open <= 0) return false;
        var items = (await container.GetAllAsync(ct).ConfigureAwait(false)).Where(i => i.SlotIndex < open).ToArray();
        var locked = items.Where(item => item.Snapshot.SortLockFlag == 1).Select(item => item.SlotIndex).ToHashSet();
        var movable = items.Where(item => !locked.Contains(item.SlotIndex)).OrderBy(item => item.SlotIndex).ToArray();
        var metadata = new Dictionary<int, InventorySortKeys>();
        if (order is InventorySortOrder.Rarity or InventorySortOrder.Level or InventorySortOrder.Type
            || space == BusinessInventorySpace.Avatar && order == InventorySortOrder.Default)
            foreach (var id in movable.Select(item => item.ItemId).Distinct())
                metadata[id] = await keys.GetAsync(id, ct).ConfigureAwait(false);
        Array.Sort(movable, (left, right) => Compare(left, right, space, order, metadata));
        var target = 0;
        var plan = new List<(ItemReference Item, int Target)>();
        foreach (var item in movable)
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

    private static int Compare(ItemReference left, ItemReference right, BusinessInventorySpace space,
        InventorySortOrder order, IReadOnlyDictionary<int, InventorySortKeys> keys)
    {
        if (space == BusinessInventorySpace.Avatar && order == InventorySortOrder.Default)
            order = InventorySortOrder.Rarity;
        var comparison = order switch
        {
            InventorySortOrder.Rarity => Rank(keys[right.ItemId].Rarity).CompareTo(Rank(keys[left.ItemId].Rarity)),
            InventorySortOrder.Level => keys[right.ItemId].Level.CompareTo(keys[left.ItemId].Level),
            InventorySortOrder.Type when space is BusinessInventorySpace.CarriedEquipment or BusinessInventorySpace.Avatar
                => keys[left.ItemId].Part.CompareTo(keys[right.ItemId].Part),
            InventorySortOrder.Type => StringComparer.OrdinalIgnoreCase.Compare(keys[left.ItemId].StackableType, keys[right.ItemId].StackableType),
            InventorySortOrder.Expiration => Expiration(left).CompareTo(Expiration(right)),
            _ => 0,
        };
        if (comparison != 0) return comparison;
        comparison = left.Snapshot.LegacyKind.CompareTo(right.Snapshot.LegacyKind);
        if (comparison != 0) return comparison;
        comparison = left.ItemId.CompareTo(right.ItemId);
        return comparison != 0 ? comparison : left.SlotIndex.CompareTo(right.SlotIndex);
    }
    private static int Rank(int rarity) => rarity switch { 4 => 6, 6 => 5, 3 => 4, 5 => 3, _ => rarity };
    private static long Expiration(ItemReference item) => item.Snapshot.Lifetime is { IsPermanent: false } lifetime ? lifetime.Deadline : long.MaxValue;
}
