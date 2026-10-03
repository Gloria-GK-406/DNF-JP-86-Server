namespace Dfo.Server.Data.Inventory;

/// <summary>
/// The stack shape a container writes when it has to produce an item row of its own.
/// </summary>
/// <remarks>New grants start with this shape. Splits preserve their source core so expiry and
/// trade restrictions cannot be removed by moving part of a pile.</remarks>
public static class StackableItemCore
{
    private static readonly ChronicleOptionState NoChronicle = new(0, 0, 0, 0, 0);

    private static readonly RandomOptionState NoRandom = new(0, 0, 0);

    private static readonly InventoryItemCoreState Empty = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        NoChronicle, NoChronicle, 0, 0, 0, 0, 0,
        NoRandom, NoRandom, NoRandom, 0, 0, 0, NoRandom,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static InventoryItemCoreState Create(byte itemKind, int itemId, int count) =>
        Empty with
        {
            ItemKind = itemKind,
            ItemId = itemId,
            Value = count,
            Marker16 = -1,
            RandomOptionChangedIndex = byte.MaxValue,
        };
}
