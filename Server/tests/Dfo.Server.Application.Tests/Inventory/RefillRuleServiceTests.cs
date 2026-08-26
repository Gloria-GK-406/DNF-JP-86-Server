using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// The four judgements the daily refill and the tutorial reward make, checked without a database.
/// </summary>
/// <remarks>
/// Each of these used to be inside the SQL that carried it out, which meant the only way to ask
/// what the server thought was to write rows and read them back. Every case here would previously
/// have needed a temporary database file; none of them needs one now, and that is the requirement
/// rather than a convenience.
///
/// The behaviour asserted is the behaviour before the rules moved. `SqliteEntryInventoryMutationRepositoryTests`
/// holds the same judgements from the other side and its assertions are not to be touched.
/// </remarks>
public sealed class RefillRuleServiceTests
{
    private static readonly IDailyRefillRuleService Refill = new DailyRefillRuleService();

    private static readonly IItemArrivalRuleService Arrival = new ItemArrivalRuleService();

    // ---------------------------------------------------------------- the day boundary

    /// <remarks>
    /// Two instants an hour apart that straddle midnight in UTC+8 are two days; the same pair
    /// written in UTC is one. A boundary quietly moved to UTC would refill twice in one evening.
    /// </remarks>
    [Fact]
    public void ADayTurnsOverAtMidnightInUtcPlusEight()
    {
        var lateOnDayOne = new DateTimeOffset(2026, 8, 12, 23, 30, 0, TimeSpan.FromHours(8));
        var earlyOnDayTwo = new DateTimeOffset(2026, 8, 13, 0, 30, 0, TimeSpan.FromHours(8));

        Assert.Equal("2026-08-12", Refill.PeriodOf(lateOnDayOne));
        Assert.Equal("2026-08-13", Refill.PeriodOf(earlyOnDayTwo));
        Assert.Equal(lateOnDayOne.UtcDateTime.Day, earlyOnDayTwo.UtcDateTime.Day);
    }

    /// <remarks>The pair to the one above: crossing midnight in UTC changes nothing.</remarks>
    [Fact]
    public void OneDayInUtcPlusEightIsOneDayHoweverUtcFalls()
    {
        var beforeUtcMidnight = new DateTimeOffset(2026, 8, 13, 7, 30, 0, TimeSpan.FromHours(8));
        var afterUtcMidnight = new DateTimeOffset(2026, 8, 13, 8, 30, 0, TimeSpan.FromHours(8));

        Assert.NotEqual(beforeUtcMidnight.UtcDateTime.Day, afterUtcMidnight.UtcDateTime.Day);
        Assert.Equal(Refill.PeriodOf(beforeUtcMidnight), Refill.PeriodOf(afterUtcMidnight));
    }

    /// <remarks>
    /// The moment is read in its own offset, not in the server's. A time handed over as UTC and the
    /// same time handed over as Seoul local are one instant and must fall in one day.
    /// </remarks>
    [Fact]
    public void TheSameInstantFallsInOneDayWhicheverOffsetItArrivesIn()
    {
        var asSeoul = new DateTimeOffset(2026, 8, 13, 7, 30, 0, TimeSpan.FromHours(8));

        Assert.Equal(Refill.PeriodOf(asSeoul), Refill.PeriodOf(asSeoul.ToUniversalTime()));
    }

    // ---------------------------------------------------------------- claim idempotency

    [Fact]
    public void ARuleNeverClaimedIsDue()
    {
        Assert.True(Refill.IsDue(claimedPeriod: null, period: "2026-08-13"));
    }

    [Fact]
    public void ARuleClaimedTodayIsNotDueAgain()
    {
        Assert.False(Refill.IsDue("2026-08-13", "2026-08-13"));
    }

    /// <remarks>
    /// Yesterday's claim does not hold today. It is compared rather than merely present, which is
    /// what makes the record a claim for one day instead of a claim for ever.
    /// </remarks>
    [Fact]
    public void ARuleClaimedOnAnotherDayIsDueAgain()
    {
        Assert.True(Refill.IsDue("2026-08-12", "2026-08-13"));
    }

    // ---------------------------------------------------------------- how many are granted

    [Theory]
    [InlineData(DailyRefillMode.RefillToTarget, 5, 100, 0, 5)]
    [InlineData(DailyRefillMode.RefillToTarget, 5, 100, 3, 2)]
    [InlineData(DailyRefillMode.RefillToTarget, 5, 100, 5, 0)]
    [InlineData(DailyRefillMode.RefillToTarget, 5, 100, 9, 0)]
    [InlineData(DailyRefillMode.RefillToTarget, 500, 100, 0, 100)]
    [InlineData(DailyRefillMode.AddUpToStackLimit, 5, 100, 0, 5)]
    [InlineData(DailyRefillMode.AddUpToStackLimit, 5, 100, 97, 3)]
    [InlineData(DailyRefillMode.AddUpToStackLimit, 5, 100, 100, 0)]
    public void TheGrantFollowsTheRulesOwnMode(
        DailyRefillMode mode, int quantity, int stackLimit, int current, int expected)
    {
        var rule = new DailyRefillRule(
            1234, quantity, DateTimeOffset.MaxValue, mode, stackLimit, 2, 65, 120);

        Assert.Equal(expected, Refill.GrantFor(rule, current));
    }

    [Fact]
    public void AnItemThatDoesNotStackGrantsNothing()
    {
        var rule = new DailyRefillRule(
            1234, 5, DateTimeOffset.MaxValue, DailyRefillMode.RefillToTarget, 0, 2, 65, 120);

        Assert.Equal(0, Refill.GrantFor(rule, 0));
    }

    // ---------------------------------------------------------------- onto a pile, or a new slot

    [Fact]
    public void ARefillTopsUpThePileTheCharacterAlreadyHas()
    {
        var arrival = Arrival.ForDailyRefill(
            [new ItemPile(70, 2)], grant: 3, slotStart: 65, slotEnd: 120, Occupied(70));

        var onto = Assert.IsType<ItemArrival.OntoExistingPile>(arrival);
        Assert.Equal(70, onto.SlotIndex);
        Assert.Equal(3, onto.Added);
        Assert.Equal(5, onto.Total);
    }

    /// <remarks>
    /// The first pile in slot order, so that two runs of the same refill agree about which one grew
    /// rather than depending on the order the rows came back.
    /// </remarks>
    [Fact]
    public void ARefillTopsUpTheFirstPileInSlotOrder()
    {
        var arrival = Arrival.ForDailyRefill(
            [new ItemPile(68, 1), new ItemPile(90, 4)], grant: 2, slotStart: 65, slotEnd: 120,
            Occupied(68, 90));

        Assert.Equal(68, Assert.IsType<ItemArrival.OntoExistingPile>(arrival).SlotIndex);
    }

    [Fact]
    public void ARefillOpensASlotOnlyWhenTheCharacterHasNone()
    {
        var arrival = Arrival.ForDailyRefill([], grant: 5, slotStart: 65, slotEnd: 120, Occupied());

        var into = Assert.IsType<ItemArrival.IntoEmptySlot>(arrival);
        Assert.Equal(65, into.SlotIndex);
        Assert.Equal(5, into.Count);
    }

    // ---------------------------------------------------------------- the band search

    [Fact]
    public void ANewPileTakesTheFirstFreeSlotOfItsOwnBand()
    {
        var arrival = Arrival.ForDailyRefill(
            [], grant: 5, slotStart: 65, slotEnd: 120, Occupied(3, 65, 66));

        Assert.Equal(67, Assert.IsType<ItemArrival.IntoEmptySlot>(arrival).SlotIndex);
    }

    /// <remarks>
    /// Slots below the band belong to equipment and the quick bar. Falling back to one of those
    /// would put the item where the client does not draw it.
    /// </remarks>
    [Fact]
    public void AFullBandDoesNotSpillIntoTheSlotsBelowIt()
    {
        var arrival = Arrival.ForDailyRefill(
            [], grant: 5, slotStart: 65, slotEnd: 66, Occupied(65, 66));

        Assert.IsType<ItemArrival.NoRoom>(arrival);
    }

    // ---------------------------------------------------------------- an ordinary grant

    /// <remarks>
    /// A grant differs from a refill: it fills existing piles only as far as the stack limit, and
    /// what will not fit opens a slot. The player who picks up a sixth potion of five expects the
    /// pile to read five and a second pile to appear, not a pile of six.
    /// </remarks>
    [Fact]
    public void AGrantFillsAPileOnlyToTheStackLimit()
    {
        var arrival = Arrival.ForGrant(
            [new ItemPile(70, 3)], count: 4, new ItemPlacement(2, 5, 65, 120), Occupied(70));

        var onto = Assert.IsType<ItemArrival.OntoExistingPile>(arrival);
        Assert.Equal(2, onto.Added);
        Assert.Equal(5, onto.Total);
    }

    [Fact]
    public void AGrantSkipsAPileThatIsAlreadyFull()
    {
        var arrival = Arrival.ForGrant(
            [new ItemPile(70, 5), new ItemPile(71, 1)], count: 2,
            new ItemPlacement(2, 5, 65, 120), Occupied(70, 71));

        Assert.Equal(71, Assert.IsType<ItemArrival.OntoExistingPile>(arrival).SlotIndex);
    }

    /// <remarks>
    /// An item whose stack limit is one never joins a pile, however many the character holds --
    /// each is its own thing, and adding to one would be adding to a piece of gear.
    /// </remarks>
    [Fact]
    public void AnItemThatDoesNotStackAlwaysOpensASlot()
    {
        var arrival = Arrival.ForGrant(
            [new ItemPile(70, 1)], count: 1, new ItemPlacement(1, 1, 65, 120), Occupied(70));

        var into = Assert.IsType<ItemArrival.IntoEmptySlot>(arrival);
        Assert.Equal(65, into.SlotIndex);
        Assert.Equal(1, into.Count);
    }

    [Fact]
    public void AGrantIntoAnEmptySlotIsCappedByTheStackLimit()
    {
        var arrival = Arrival.ForGrant(
            [], count: 9, new ItemPlacement(2, 5, 65, 120), Occupied());

        Assert.Equal(5, Assert.IsType<ItemArrival.IntoEmptySlot>(arrival).Count);
    }

    /// <remarks>A stack limit of zero is the archive saying nothing, and caps nothing.</remarks>
    [Fact]
    public void AGrantWithNoDeclaredStackLimitIsNotCapped()
    {
        var arrival = Arrival.ForGrant(
            [], count: 9, new ItemPlacement(2, 0, 65, 120), Occupied());

        Assert.Equal(9, Assert.IsType<ItemArrival.IntoEmptySlot>(arrival).Count);
    }

    /// <remarks>
    /// The quick bar is seeded as well as the band. A consumable is offered slots 3 to 8 before its
    /// own band, so a test that filled only the band would be asserting that six free slots are no
    /// room at all.
    /// </remarks>
    [Fact]
    public void AGrantWithNowhereToGoAnswersNoRoom()
    {
        var arrival = Arrival.ForGrant(
            [], count: 1, new ItemPlacement(2, 5, 65, 66), Occupied(3, 4, 5, 6, 7, 8, 65, 66));

        Assert.IsType<ItemArrival.NoRoom>(arrival);
    }

    /// <summary>A consumable is offered the quick bar first, and its own band after it.</summary>
    [Fact]
    public void AConsumableIsOfferedTheQuickBarBeforeItsOwnBand()
    {
        var placement = new ItemPlacement(2, 5, 65, 66);

        Assert.Equal(
            3,
            Assert.IsType<ItemArrival.IntoEmptySlot>(
                Arrival.ForGrant([], count: 1, placement, Occupied())).SlotIndex);
        Assert.Equal(
            65,
            Assert.IsType<ItemArrival.IntoEmptySlot>(
                Arrival.ForGrant([], count: 1, placement, Occupied(3, 4, 5, 6, 7, 8))).SlotIndex);
    }

    /// <summary>Everything else is offered its own band first and the bar only as a fallback.</summary>
    /// <remarks>
    /// The other half of the reference server's own branch. A quest item belongs in the quest band
    /// and may sit on the bar; a consumable prefers the bar. Both orders come from
    /// <c>InventoryInsertService.TryPlanInsertByDefaultRule</c>.
    /// </remarks>
    [Fact]
    public void AQuestItemIsOfferedItsOwnBandBeforeTheQuickBar()
    {
        var placement = new ItemPlacement(4, 5, 177, 178);

        Assert.Equal(
            177,
            Assert.IsType<ItemArrival.IntoEmptySlot>(
                Arrival.ForGrant([], count: 1, placement, Occupied())).SlotIndex);
        Assert.Equal(
            3,
            Assert.IsType<ItemArrival.IntoEmptySlot>(
                Arrival.ForGrant([], count: 1, placement, Occupied(177, 178))).SlotIndex);
    }

    /// <summary>Equipment never reaches the bar, however full its own band is.</summary>
    /// <remarks>
    /// The bar holds things that are used. The reference server excludes anything unstackable from
    /// this decision entirely, which is the same outcome by another route.
    /// </remarks>
    [Fact]
    public void EquipmentIsNeverOfferedTheQuickBar()
    {
        var arrival = Arrival.ForGrant(
            [], count: 1, new ItemPlacement(1, 1, 9, 10), Occupied(9, 10));

        Assert.IsType<ItemArrival.NoRoom>(arrival);
    }

    private static IReadOnlySet<int> Occupied(params int[] slots) => slots.ToHashSet();
}
