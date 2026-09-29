using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class InventorySortDomainService(
    ICharacterBusinessInventoryDomainService inventories, IInventorySortKeySource keys)
    : IInventorySortDomainService
{
    public async ValueTask<bool> SortAsync(CharacterId character, BusinessInventorySpace space,
        InventorySortOrder order, CancellationToken ct)
    {
        if (!Enum.IsDefined(order) || space is not (BusinessInventorySpace.CarriedEquipment
            or BusinessInventorySpace.Consumable or BusinessInventorySpace.Material
            or BusinessInventorySpace.Quest or BusinessInventorySpace.ExpertMaterial
            or BusinessInventorySpace.AvatarEmblem or BusinessInventorySpace.Avatar
            or BusinessInventorySpace.CreatureEquipment or BusinessInventorySpace.CreatureConsumable))
            return false;
        var container = inventories.GetStore(character, BusinessInventoryOwnership.StoreOf(space)).GetContainer(space);
        var items = await container.ReadAllAsync(ct).ConfigureAwait(false);
        var locked = items.Where(item => item.Core.SortLockFlag == 1).Select(item => item.SlotIndex).ToHashSet();
        var movable = items.Where(item => !locked.Contains(item.SlotIndex)).OrderBy(item => item.SlotIndex).ToArray();
        var metadata = new Dictionary<int, InventorySortKeys>();
        if (order is InventorySortOrder.Rarity or InventorySortOrder.Level or InventorySortOrder.Type
            || space == BusinessInventorySpace.Avatar && order == InventorySortOrder.Default)
            foreach (var id in movable.Select(item => item.ItemId).Distinct())
                metadata[id] = await keys.GetAsync(id, ct).ConfigureAwait(false);
        Array.Sort(movable, (left, right) => Compare(left, right, space, order, metadata));
        var target = 0;
        var plan = new List<(ContainerSlotItem Item, int Target)>();
        foreach (var item in movable)
        {
            while (locked.Contains(target)) target++;
            plan.Add((item, target++));
        }
        if (plan.All(move => move.Item.SlotIndex == move.Target)) return true;
        // Snapshot every source before staging any write. Explicit source positions preserve
        // distinct instance identities even when multiple equipment cores have identical values.
        foreach (var move in plan) await container.ClearAsync(move.Item.SlotIndex, ct).ConfigureAwait(false);
        foreach (var move in plan)
            if (move.Item.IsPile)
                await container.PutAsync(move.Target, move.Item.Core, ct).ConfigureAwait(false);
            else
                await container.PutTransferredAsync(move.Target, move.Item.Core, container,
                    move.Item.SlotIndex, ct).ConfigureAwait(false);
        await container.FlushAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static int Compare(ContainerSlotItem left, ContainerSlotItem right, BusinessInventorySpace space,
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
        comparison = left.ItemKind.CompareTo(right.ItemKind);
        if (comparison != 0) return comparison;
        comparison = left.ItemId.CompareTo(right.ItemId);
        return comparison != 0 ? comparison : left.SlotIndex.CompareTo(right.SlotIndex);
    }
    private static int Rank(int rarity) => rarity switch { 4 => 6, 6 => 5, 3 => 4, 5 => 3, _ => rarity };
    private static int Expiration(ContainerSlotItem item) => item.Core.ExpireTime > 0 ? item.Core.ExpireTime : int.MaxValue;
}
