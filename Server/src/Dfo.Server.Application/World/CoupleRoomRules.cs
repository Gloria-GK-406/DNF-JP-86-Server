using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

/// <summary>How the couple room is entered, left, instanced and stored.</summary>
/// <remarks>Pure; reached only through <see cref="IEntryPlacementRuleService"/>.</remarks>
internal static class CoupleRoomRules
{
    public static bool IsHouseholdTown(int townId) => CoupleRoomTown.Is(townId);

    public static int? AreaInstance(int townId, int? householdInstance) =>
        !IsHouseholdTown(townId) ? 0
        : householdInstance is > 0 ? householdInstance
        : null;

    public static TownCrossingRefusal? RefuseCrossing(TownCrossingFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var entering = IsHouseholdTown(facts.TownId);
        if (entering
            && (facts.AreaId != 0
                || !IsHouseholdTown(facts.Standing.TownId) && !facts.StandingAtEntrySource
                || facts.HouseholdInstance is not > 0))
            return TownCrossingRefusal.CoupleRoomEntry;
        if (!entering && IsHouseholdTown(facts.Standing.TownId)
            && (facts.WayBack is not { } home || home.TownId != facts.TownId || home.AreaId != facts.AreaId))
            return TownCrossingRefusal.CoupleRoomExit;
        return null;
    }

    public static CharacterPositionState StoredPositionAfter(
        CharacterPositionState standing, CharacterPositionState destination) =>
        IsHouseholdTown(destination.TownId) ? standing : destination;

    public static TownHousingChange HousingChangeOf(int fromTownId, int toTownId) =>
        (IsHouseholdTown(fromTownId), IsHouseholdTown(toTownId)) switch
        {
            (false, true) => TownHousingChange.Enters,
            (true, false) => TownHousingChange.Leaves,
            _ => TownHousingChange.None,
        };
}
