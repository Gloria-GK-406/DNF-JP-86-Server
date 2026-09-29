using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game;

internal static class ItemGrantWireProjection
{
    private static readonly InventorySlotMappingRuleService Slots = new();

    public static GameSlot Slot(ItemGrantOutcome.CharacterItem grant, int localIndex) =>
        Slots.ToGameSlot(new BusinessSlot(grant.Container.Space, localIndex));

    public static int Index(ItemGrantOutcome.CharacterItem grant, int localIndex) =>
        Slot(grant, localIndex).Index;
}
