using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game;

internal static class ItemGrantWireProjection
{
    public static GameSlot Slot(ItemDeliveryAddition grant) => A21InventoryLayout.ToGameSlot(grant.Slot);
    public static int Index(ItemDeliveryAddition grant) => Slot(grant).Index;
    public static InventoryRuntimeValueState? Shared(ItemDeliveryAddition grant) =>
        grant.Slot.Space == BusinessInventorySpace.SharedMaterial
            ? new(Index(grant), grant.Snapshot.ItemId, grant.Snapshot.Count) : null;

    public static GameSlot Slot(BusinessItemAdditionResult grant, int localIndex) =>
        A21InventoryLayout.ToGameSlot(new BusinessSlot(grant.Container.Space, localIndex));

    public static int Index(BusinessItemAdditionResult grant, int localIndex) =>
        Slot(grant, localIndex).Index;
    public static async ValueTask<InventoryRuntimeValueState?> SharedAsync(BusinessItemAdditionResult grant, CancellationToken ct)
    {
        if (grant.Container.Space != BusinessInventorySpace.SharedMaterial) return null;
        var index = grant.Placement switch
        {
            ItemPlacementOutcome.AddedToPile value => value.SlotIndex,
            ItemPlacementOutcome.PutInEmptySlot value => value.SlotIndex,
            _ => -1,
        };
        if (index < 0) return null;
        var item = await grant.Container.GetAsync(index, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Granted shared-material slot disappeared before commit.");
        return new(Index(grant, index), item.ItemId, item.Count);
    }

    /// <summary>The kill-effect shape of granted quest items: each touched wire slot once, as it now stands.</summary>
    public static async ValueTask<(CommittedInventoryUpdateState? Items, IReadOnlyList<InventoryRuntimeValueState> Shared)> QuestItemsAsync(
        IReadOnlyList<ItemDeliveryAddition> additions, DateTimeOffset at, CancellationToken ct)
    {
        var changed = new Dictionary<int, ItemDeliveryAddition>();
        foreach (var addition in additions) changed[Index(addition)] = addition;
        var items = new List<InventoryItemState>();
        var shared = new List<InventoryRuntimeValueState>();
        foreach (var (slot, source) in changed.OrderBy(x => x.Key))
        {
            var item = await source.Container.GetAsync(source.Slot.Index, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Granted quest item disappeared before commit.");
            if (source.Container.Space == BusinessInventorySpace.SharedMaterial) shared.Add(new(slot, item.ItemId, item.Count));
            else items.Add(new(null, slot, item.Snapshot.ToCore()));
        }
        return (items.Count == 0 ? null : new(InventoryContainerType.Main, items, at), shared);
    }
}
