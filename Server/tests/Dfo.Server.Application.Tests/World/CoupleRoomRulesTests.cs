using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.World;

[Trait("Category", "Unit")]
public sealed class CoupleRoomRulesTests
{
    private static readonly CharacterPositionState Seria = new(3, 2, 100, 200, 5);
    private static readonly CharacterPositionState Inside = new(101, 0, 50, 60, 5);

    [Theory]
    [InlineData(3, null, 0)]
    [InlineData(101, 42, 42)]
    [InlineData(101, 0, null)]
    [InlineData(101, null, null)]
    public void Only_the_couple_room_takes_its_instance_from_the_household(int town, int? household, int? expected) =>
        Assert.Equal(expected, CoupleRoomRules.AreaInstance(town, household));

    [Fact]
    public void Entering_from_an_entry_source_with_a_household_is_allowed() =>
        Assert.Null(CoupleRoomRules.RefuseCrossing(new(Seria, 101, 0, true, 42, null)));

    [Theory]
    [InlineData(1, true, 42)]
    [InlineData(0, false, 42)]
    [InlineData(0, true, null)]
    [InlineData(0, true, 0)]
    public void Entry_needs_area_zero_an_entry_source_and_a_household(int area, bool source, int? household) =>
        Assert.Equal(TownCrossingRefusal.CoupleRoomEntry,
            CoupleRoomRules.RefuseCrossing(new(Seria, 101, area, source, household, null)));

    [Theory]
    [InlineData(3, 2, null)]
    [InlineData(3, 1, TownCrossingRefusal.CoupleRoomExit)]
    [InlineData(4, 2, TownCrossingRefusal.CoupleRoomExit)]
    public void The_couple_room_is_left_only_for_where_it_was_entered_from(int town, int area, TownCrossingRefusal? expected) =>
        Assert.Equal(expected, CoupleRoomRules.RefuseCrossing(new(Inside, town, area, false, null, Seria)));

    [Fact]
    public void Leaving_without_a_remembered_way_back_is_refused() =>
        Assert.Equal(TownCrossingRefusal.CoupleRoomExit,
            CoupleRoomRules.RefuseCrossing(new(Inside, 3, 2, false, null, null)));

    [Fact]
    public void Ordinary_crossings_are_not_judged() =>
        Assert.Null(CoupleRoomRules.RefuseCrossing(new(Seria, 4, 1, false, null, null)));

    [Fact]
    public void A_couple_room_position_is_never_stored()
    {
        Assert.Equal(Seria, CoupleRoomRules.StoredPositionAfter(Seria, Inside));
        Assert.Equal(Seria, CoupleRoomRules.StoredPositionAfter(Inside, Seria));
    }

    [Theory]
    [InlineData(3, 101, TownHousingChange.Enters)]
    [InlineData(101, 3, TownHousingChange.Leaves)]
    [InlineData(3, 4, TownHousingChange.None)]
    public void Crossing_into_or_out_of_the_couple_room_starts_or_ends_the_lease(int from, int to, TownHousingChange expected) =>
        Assert.Equal(expected, CoupleRoomRules.HousingChangeOf(from, to));
}
