using System.Globalization;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// Where an item goes when it arrives.
/// </summary>
/// <remarks>
/// Both answers are decided from what the character holds and what the archive says, and neither
/// touches a store. That is the whole point of them being here: while they were inside the SQL that
/// carried them out, the only way to ask what the server thought was to write rows and read back.
/// </remarks>
internal sealed class ItemArrivalRuleService : IItemArrivalRuleService
{
    /// <remarks>
    /// A refill joins whatever pile is there, however large. It does not consult the stack limit
    /// because the rule's own quantity is already bounded by it before this is asked -- see
    /// <see cref="DailyRefillRuleService.GrantFor"/>.
    /// </remarks>
    public ItemArrival ForDailyRefill(
        IReadOnlyList<ItemPile> present,
        int grant,
        int slotStart,
        int slotEnd,
        IReadOnlySet<int> occupiedSlots)
    {
        ArgumentNullException.ThrowIfNull(present);
        ArgumentNullException.ThrowIfNull(occupiedSlots);

        if (First(present) is { } pile)
        {
            return new ItemArrival.OntoExistingPile(
                pile.SlotIndex, grant, checked(pile.Count + grant));
        }

        return FirstFreeSlot(slotStart, slotEnd, occupiedSlots) is { } slot
            ? new ItemArrival.IntoEmptySlot(slot, grant)
            : new ItemArrival.NoRoom();
    }

    /// <remarks>
    /// A grant fills piles only as far as the item's stack limit and opens a slot for what will not
    /// fit. A player who picks up a sixth potion of five expects the pile to stay at five and a
    /// second one to appear beside it.
    /// </remarks>
    public ItemArrival ForGrant(
        IReadOnlyList<ItemPile> present,
        int count,
        ItemPlacement placement,
        IReadOnlySet<int> occupiedSlots)
    {
        ArgumentNullException.ThrowIfNull(present);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(occupiedSlots);

        if (placement.StackLimit > 1)
        {
            foreach (var pile in present.OrderBy(value => value.SlotIndex))
            {
                if (pile.Count >= placement.StackLimit)
                {
                    continue;
                }

                var added = Math.Min(count, placement.StackLimit - pile.Count);
                return new ItemArrival.OntoExistingPile(
                    pile.SlotIndex, added, pile.Count + added);
            }
        }

        if (FirstFreeSlot(placement.SlotStart, placement.SlotEnd, occupiedSlots) is not { } slot)
        {
            return new ItemArrival.NoRoom();
        }

        // A stack limit of zero is the archive declaring nothing, which caps nothing.
        var placed = placement.StackLimit > 0 ? Math.Min(count, placement.StackLimit) : count;
        return new ItemArrival.IntoEmptySlot(slot, placed);
    }

    /// <summary>The pile that grows, chosen by slot so that two runs agree about which one it is.</summary>
    private static ItemPile? First(IReadOnlyList<ItemPile> present) =>
        present.Count == 0 ? null : present.MinBy(pile => pile.SlotIndex);

    /// <remarks>
    /// Only within the band. Slots below it belong to equipment and the quick bar, and an item that
    /// spilled into one of those is an item the client does not draw where the player looks.
    /// </remarks>
    private static int? FirstFreeSlot(int slotStart, int slotEnd, IReadOnlySet<int> occupiedSlots)
    {
        if (slotStart < 0 || slotEnd < slotStart || slotEnd > ushort.MaxValue)
        {
            throw new InvalidDataException($"Item slot range {slotStart}-{slotEnd} is invalid.");
        }

        for (var slot = slotStart; slot <= slotEnd; slot++)
        {
            if (!occupiedSlots.Contains(slot))
            {
                return slot;
            }
        }

        return null;
    }
}

/// <summary>
/// Which day a refill belongs to, and whether it is still owed.
/// </summary>
/// <remarks>
/// The day is the one in UTC+8, which is where this game's day has always turned over. Reading it
/// from the moment's own offset rather than from the server's clock is what keeps the answer the
/// same wherever the host happens to be.
/// </remarks>
internal sealed class DailyRefillRuleService : IDailyRefillRuleService
{
    private static readonly TimeSpan DayBoundaryOffset = TimeSpan.FromHours(8);

    public string PeriodOf(DateTimeOffset preparedAt) =>
        preparedAt.ToOffset(DayBoundaryOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <remarks>
    /// The recorded day is compared, not merely looked for. A record that only said "claimed" would
    /// be a claim for ever rather than a claim for one day.
    /// </remarks>
    public bool IsDue(string? claimedPeriod, string period) =>
        !string.Equals(claimedPeriod, period, StringComparison.Ordinal);

    public int GrantFor(DailyRefillRule rule, int currentCount)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.StackLimit <= 0)
        {
            return 0;
        }

        currentCount = Math.Max(0, currentCount);
        return rule.Mode switch
        {
            DailyRefillMode.RefillToTarget =>
                Math.Max(0, Math.Min(rule.Quantity, rule.StackLimit) - currentCount),
            DailyRefillMode.AddUpToStackLimit =>
                Math.Min(rule.Quantity, Math.Max(0, rule.StackLimit - currentCount)),
            _ => throw new InvalidDataException($"Daily refill mode {(int)rule.Mode} is invalid."),
        };
    }
}
