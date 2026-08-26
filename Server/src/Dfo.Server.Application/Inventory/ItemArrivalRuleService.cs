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

        foreach (var (start, end) in BandsFor(placement))
        {
            if (FirstFreeSlot(start, end, occupiedSlots) is not { } slot)
            {
                continue;
            }

            // A stack limit of zero is the archive declaring nothing, which caps nothing.
            var placed = placement.StackLimit > 0 ? Math.Min(count, placement.StackLimit) : count;
            return new ItemArrival.IntoEmptySlot(slot, placed);
        }

        return new ItemArrival.NoRoom();
    }

    /// <summary>What the archive's equipment index makes every worn item.</summary>
    private const byte EquipmentKind = 1;

    /// <summary>What a potion is, and the one kind the bar is offered before the bag.</summary>
    private const byte ConsumableKind = 2;

    /// <summary>
    /// The bands an arriving item is offered, in the order it is offered them.
    /// </summary>
    /// <remarks>
    /// <b>A consumable is offered the quick-slot bar first.</b> That is not a nicety: the tutorial
    /// hands over a potion and then tells the player to press its key, and a potion that landed in
    /// the bag leaves the bar empty and the step unfinishable -- which is what a live A21 session
    /// showed on 2026-08-26, our acknowledgement naming slot 65 where the reference server's named
    /// slot 3. The reference builds the order the same way, in
    /// <c>InventoryInsertService.TryPlanInsertByDefaultRule</c>.
    /// <para>
    /// Everything else that may occupy a quick slot is offered its own band first and the bar only
    /// as a fallback, which is the same source's other branch. Creatures and what they wear or eat
    /// may not occupy one at all, and equipment never reaches here: the bar holds things that are
    /// used, and the reference excludes anything unstackable from this whole decision.
    /// </para>
    /// </remarks>
    private static IEnumerable<(int Start, int End)> BandsFor(ItemPlacement placement)
    {
        var own = (placement.SlotStart, placement.SlotEnd);
        if (placement.ItemKind == EquipmentKind
            || !ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind))
        {
            yield return own;
            yield break;
        }

        var bar = (ItemSlotRuleService.QuickSlotStart, ItemSlotRuleService.QuickSlotEnd);
        if (placement.ItemKind == ConsumableKind)
        {
            yield return bar;
            yield return own;
            yield break;
        }

        yield return own;
        yield return bar;
    }

    /// <summary>The pile that grows, chosen by slot so that two runs agree about which one it is.</summary>
    private static ItemPile? First(IReadOnlyList<ItemPile> present) =>
        present.Count == 0 ? null : present.MinBy(pile => pile.SlotIndex);

    /// <remarks>
    /// Only within the band it is given. Which bands an item is given, and in what order, is
    /// <see cref="BandsFor"/>'s business; an item that landed outside all of them is one the client
    /// does not draw where the player looks.
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
