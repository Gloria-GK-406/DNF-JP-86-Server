using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>What one slot already holds of the item that is arriving.</summary>
public sealed record ItemPile(BusinessSlot Slot, int Count);

/// <summary>Where an arriving pile of items ends up.</summary>
/// <remarks>
/// Three outcomes rather than a slot and a count, because "no room" is not a slot and answering it
/// with one would leave every caller inventing its own sentinel. Which of the three it is also
/// decides what the caller writes: a row updated, a row inserted, or nothing at all.
/// </remarks>
public abstract record ItemArrival
{
    private ItemArrival()
    {
    }

    /// <summary>Added to a pile the character already had.</summary>
    /// <param name="Total">What that pile holds afterwards.</param>
    public sealed record OntoExistingPile(BusinessSlot Slot, int Added, int Total) : ItemArrival;

    /// <summary>Put in a slot of its own band that was empty.</summary>
    public sealed record IntoEmptySlot(BusinessSlot Slot, int Count) : ItemArrival;

    /// <summary>The band this item belongs to has nothing free.</summary>
    public sealed record NoRoom : ItemArrival;
}

/// <summary>
/// Where an item goes when it arrives, decided without reference to any store.
/// </summary>
/// <remarks>
/// These were decisions the database made about itself: whether a grant joined an existing pile or
/// opened a slot, and which slot of the item's band was free. Neither needs a database to answer --
/// both are questions about what the character holds and what the archive says about the item --
/// and while they lived inside the SQL there was no way to check them except by writing rows.
/// </remarks>
public interface IItemArrivalRuleService
{
    /// <summary>
    /// Where a daily refill's grant goes.
    /// </summary>
    /// <remarks>
    /// A refill tops up the pile the character already has, whatever its size, and only opens a
    /// slot when they have none. It differs from an ordinary grant, which fills piles up to the
    /// item's stack limit and spills into a new slot when they are full: a refill's own quantity is
    /// already bounded by the rule, so it has nothing to spill.
    /// </remarks>
    ItemArrival ForDailyRefill(
        IReadOnlyList<ItemPile> present,
        int grant,
        BusinessSlotRange destination,
        IReadOnlySet<BusinessSlot> occupiedSlots);

    /// <summary>Where an item the game hands the character goes.</summary>
    ItemArrival ForGrant(
        IReadOnlyList<ItemPile> present,
        int count,
        ItemPlacement placement,
        IReadOnlySet<BusinessSlot> occupiedSlots);
}

/// <summary>
/// Which day a refill belongs to, and whether it is still owed.
/// </summary>
/// <remarks>
/// The day boundary is the judgement with the least visible failure: moved to UTC it would hand
/// every player a second refill in the middle of their evening, and nothing about the code would
/// look wrong. Answering it here is what lets it be checked against a clock instead of a database.
/// </remarks>
public interface IDailyRefillRuleService
{
    /// <summary>The day this moment falls in, as the claim record spells it.</summary>
    string PeriodOf(DateTimeOffset preparedAt);

    /// <summary>Whether a rule is still owed, given the day already recorded against it.</summary>
    /// <param name="claimedPeriod">Null when nothing has ever been recorded for it.</param>
    bool IsDue(string? claimedPeriod, string period);

    /// <summary>How many the rule grants, given what the character already holds of it.</summary>
    int GrantFor(DailyRefillRule rule, int currentCount);
}
