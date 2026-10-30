using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class InventorySortRuleService(IInventorySortKeySource keys) : IInventorySortRuleService
{
    public bool IsSortable(BusinessInventorySpace space) => IsSortableSpace(space);

    public async ValueTask<IReadOnlyList<ItemReference>> OrderAsync(BusinessInventorySpace space,
        InventorySortOrder order, IReadOnlyList<ItemReference> movable, CancellationToken ct)
    {
        var metadata = new Dictionary<int, InventorySortKeys>();
        if (order is InventorySortOrder.Rarity or InventorySortOrder.Level or InventorySortOrder.Type
            || space == BusinessInventorySpace.Avatar && order == InventorySortOrder.Default)
            foreach (var id in movable.Select(item => item.ItemId).Distinct())
                metadata[id] = await keys.GetAsync(id, ct).ConfigureAwait(false);
        var ordered = movable.ToArray();
        Array.Sort(ordered, (left, right) => Compare(left, right, space, order, metadata));
        return ordered;
    }

    private static bool IsSortableSpace(BusinessInventorySpace space) => space is
        BusinessInventorySpace.CarriedEquipment or BusinessInventorySpace.Consumable
        or BusinessInventorySpace.Material or BusinessInventorySpace.Quest or BusinessInventorySpace.ExpertMaterial
        or BusinessInventorySpace.AvatarEmblem or BusinessInventorySpace.Avatar
        or BusinessInventorySpace.CreatureEquipment or BusinessInventorySpace.CreatureConsumable
        or BusinessInventorySpace.PersonalCargo or BusinessInventorySpace.AccountCargo;

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
