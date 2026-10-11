using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game;

internal static class ItemGrantWireProjection
{
    private static readonly InventorySlotMappingRuleService Slots = new();
    public static GameSlot Slot(ItemDeliveryAddition grant) => Slots.ToGameSlot(grant.Slot);
    public static int Index(ItemDeliveryAddition grant) => Slot(grant).Index;
    public static InventoryRuntimeValueState? Shared(ItemDeliveryAddition grant) =>
        grant.Slot.Space == BusinessInventorySpace.SharedMaterial
            ? new(Index(grant), grant.Snapshot.ItemId, grant.Snapshot.Count) : null;

    public static GameSlot Slot(BusinessItemAdditionResult grant, int localIndex) =>
        Slots.ToGameSlot(new BusinessSlot(grant.Container.Space, localIndex));

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
}
