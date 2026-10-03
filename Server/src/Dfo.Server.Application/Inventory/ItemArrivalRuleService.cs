using System.Globalization;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

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
        BusinessSlotRange destination,
        IReadOnlySet<BusinessSlot> occupiedSlots)
    {
        ArgumentNullException.ThrowIfNull(present);
        ArgumentNullException.ThrowIfNull(occupiedSlots);

        if (First(present) is { } pile)
        {
            return new ItemArrival.OntoExistingPile(
                pile.Slot, grant, checked(pile.Count + grant));
        }

        return FirstFreeSlot(destination, occupiedSlots) is { } slot
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
        IReadOnlySet<BusinessSlot> occupiedSlots)
    {
        ArgumentNullException.ThrowIfNull(present);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(occupiedSlots);

        // A declared limit of zero is the archive declaring nothing, which caps nothing -- the same
        // reading the empty-slot branch below already used, and the reference server's in both the
        // places it resolves a limit. Until 2026-08-30 this branch read zero as "does not stack"
        // instead, so the two halves of this method disagreed: a crystal shard picked up twice
        // opened two slots because its file declares no [stack limit], which almost no stackable
        // in the archive does. Equipment is unaffected -- its limit is one, and one still piles
        // onto nothing.
        var stackLimit = placement.StackLimit > 0 ? placement.StackLimit : int.MaxValue;
        if (placement.Destination.Space == BusinessInventorySpace.SharedMaterial)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            var current = present.Where(pile => placement.Destination.Contains(pile.Slot)).Sum(pile => (long)pile.Count);
            if (current + count > stackLimit) return new ItemArrival.NoRoom();
        }
        if (stackLimit > 1)
        {
            foreach (var pile in present.OrderBy(value => value.Slot.Space).ThenBy(value => value.Slot.Index))
            {
                if (pile.Count >= stackLimit)
                {
                    continue;
                }

                var added = Math.Min(count, stackLimit - pile.Count);
                return new ItemArrival.OntoExistingPile(
                    pile.Slot, added, pile.Count + added);
            }
        }

        foreach (var range in BandsFor(placement))
        {
            if (FirstFreeSlot(range, occupiedSlots) is not { } slot)
            {
                continue;
            }

            var placed = Math.Min(count, stackLimit);
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
    private static IEnumerable<BusinessSlotRange> BandsFor(ItemPlacement placement)
    {
        var own = placement.Destination;
        if (own.Space == BusinessInventorySpace.SharedMaterial || placement.ItemKind == EquipmentKind
            || !ItemSlotRuleService.AllowsQuickSlot(placement))
        {
            yield return own;
            yield break;
        }

        var bar = new BusinessSlotRange(BusinessInventorySpace.QuickSlot, 0, 5);
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
        present.Count == 0 ? null : present.MinBy(pile => (pile.Slot.Space, pile.Slot.Index));

    /// <remarks>
    /// Only within the band it is given. Which bands an item is given, and in what order, is
    /// <see cref="BandsFor"/>'s business; an item that landed outside all of them is one the client
    /// does not draw where the player looks.
    /// </remarks>
    private static BusinessSlot? FirstFreeSlot(
        BusinessSlotRange range,
        IReadOnlySet<BusinessSlot> occupiedSlots)
    {
        for (var index = range.FirstIndex; index <= range.LastIndex; index++)
        {
            var slot = new BusinessSlot(range.Space, index);
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
