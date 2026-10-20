using System.Collections.Immutable;

namespace Dfo.Server.Data.Characters;

public sealed record CollectionBoxContents(byte BoxId, ImmutableArray<int> ItemIds);
public sealed record CollectionBoxSnapshot(byte SelectedBoxId, ImmutableArray<CollectionBoxContents> Boxes,
    ImmutableArray<string> Diagnostics);

/// <summary>Stable server storage coordinates, independent of the current PVF grid dimensions.</summary>
public static class CollectionBoxStorage
{
    public static int Slot(int boxId, int slotIndex) => boxId is >= 1 and <= 255 && slotIndex is >= 0 and <= 255
        ? boxId * 256 + slotIndex : throw new ArgumentOutOfRangeException(nameof(slotIndex));
    public static (int BoxId, int SlotIndex) Decode(int slot) => slot is >= 256 and <= 65535
        ? (slot / 256, slot % 256) : throw new InvalidDataException("Invalid collection storage coordinate.");
}
