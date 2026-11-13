namespace Dfo.Server.Data.Characters;

/// <summary>An account's adventure group (冒险团): its level and the points behind it.</summary>
/// <remarks>
/// Derived, never stored: it is worked out from the levels of the account's characters each time it
/// is asked for. The wire carries it as a <c>u8</c> level and an <c>i32</c> point total.
/// </remarks>
public readonly record struct AdventureGroupStanding(byte Level, int Point)
{
    public static AdventureGroupStanding None => default;
}
