namespace Dfo.Server.Abstractions.Content;

/// <summary>
/// Where an item belongs when it arrives, and how much of it one slot holds.
/// </summary>
/// <param name="SlotStart">
/// First slot of the range this kind occupies, inclusive. The main inventory is not one flat run:
/// consumables, materials and cube fragments each have their own band, and an item placed outside
/// its own band is one the client will not draw where the player looks for it.
/// </param>
public sealed record ItemPlacement(byte ItemKind, int StackLimit, int SlotStart, int SlotEnd);

/// <summary>Answers where an item goes, from the archive's own item definition.</summary>
public interface IItemPlacementDefinitionSource
{
    /// <summary>
    /// The placement for this item, or null when the archive declares no item that can be held
    /// under that identifier.
    /// </summary>
    ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken);
}
