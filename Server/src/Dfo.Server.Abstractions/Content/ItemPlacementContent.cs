using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Content;

/// <summary>
/// What a template is: whether two of it can be told apart, and which sort of thing it is.
/// </summary>
/// <remarks>
/// The archive is the only thing that answers this. <c>equipment/equipment.lst</c> names every
/// template that is an individual piece, and each of those declares an <c>[equipment type]</c>
/// naming the slot it is worn in; the eleven names ending in <c>avatar</c> are the avatar slots
/// and <c>[creature]</c> is the contract creature. Everything the archive lists under
/// <c>stackable/stackable.lst</c>, and everything it does not declare at all, is a pile.
/// <para>
/// <b>Not the kind byte, and not derived from it.</b> The kind a row carries is a number the
/// client is sent; it is written into storage once and never checked again, and a caller is free
/// to hand a write a core saying anything at all. So this is answered from the template alone, and
/// nothing that decides it may be handed a stored row.
/// </para>
/// <para>
/// This enum exists because the two sources once disagreed: the equipment index handed out the
/// constant 1 while storage recorded a 5 for a contract creature and an 8 for an avatar. Both
/// pairs are answered now and no template is left where the two disagree, but the byte is still
/// whatever a caller put in it, so the template stays the only thing asked.
/// </para>
/// </remarks>
public enum ItemWorldClass
{
    /// <summary>
    /// Two of it are the same thing: a pile with a count, and nothing else of its own.
    /// </summary>
    /// <remarks>
    /// Also the answer for a template the archive does not declare. That is the reading both
    /// callers of the equipment kind already took: an unknown identifier is banked as an ordinary
    /// stackable rather than refused.
    /// </remarks>
    Interchangeable = 0,

    /// <summary>A worn piece: one of it per slot, and its own enchantments and rolled options.</summary>
    Equipment = 1,

    /// <summary>An avatar: a worn piece that also carries its own colours and jewels.</summary>
    Avatar = 2,

    /// <summary>A contract creature: a worn piece that also carries its own instance state.</summary>
    Creature = 3,
}

/// <summary>
/// Where an item belongs when it arrives, how much of it one slot holds, and what sort of thing
/// it is.
/// </summary>
/// <param name="Destination">
/// The one business-space range this kind occupies.  It contains no protocol list number or
/// persistence offset; those are restored only at their respective adapters.
/// </param>
public sealed record ItemPlacement(
    byte ItemKind,
    int StackLimit,
    BusinessSlotRange Destination)
{
    /// <summary>
    /// What the archive says this template is.
    /// </summary>
    /// <remarks>
    /// Answered together with the band because both come out of the same lookup, and asking them
    /// apart would open the archive twice for one template. Written rather than positional so
    /// that a placement described for some other purpose -- a band, a stack limit -- says
    /// <see cref="ItemWorldClass.Interchangeable"/> by omission, which is the same answer an
    /// undeclared template gets.
    /// </remarks>
    public ItemWorldClass WorldClass { get; init; }
}

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
