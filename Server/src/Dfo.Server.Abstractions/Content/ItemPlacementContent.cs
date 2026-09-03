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

/// <summary>
/// How much gold a character may carry at a level, as the archive declares it.
/// </summary>
/// <remarks>
/// <c>etc/(r)goldlimitbylevel.etc</c> holds <c>[gold limit from level]</c>: a hundred pairs of a
/// level and the most gold a character standing there may carry. It rises from a hundred thousand
/// at level one to four hundred million from level sixty on.
/// <para>
/// <b>This is where the cap actually comes from.</b> The character row has a <c>gold_carry_limit</c>
/// column, but nothing in this server has ever written to it, so it reads zero for every character
/// -- and a cap of zero, taken at face value, would clamp every player's gold to nothing. The
/// stored column is an upgrade a player buys on top of this floor; until something sells that
/// upgrade, the floor is the whole of it.
/// </para>
/// </remarks>
public interface IGoldCarryLimitSource
{
    /// <summary>The most gold a character standing at that level may carry.</summary>
    /// <remarks>
    /// A level the table does not name takes the nearest declared level below it, which is how a
    /// table of a hundred rows answers for a character above its last one.
    /// </remarks>
    int BaseLimitFor(int characterLevel);
}
