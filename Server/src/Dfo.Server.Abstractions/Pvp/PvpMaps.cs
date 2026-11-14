namespace Dfo.Server.Abstractions.Pvp;

/// <summary>The archive's duel map order; a room's map is an index into it.</summary>
/// <param name="Order">Map ids by index. Index 0 is the random choice rather than a map a room plays on.</param>
public sealed record PvpMapCatalog(IReadOnlyList<int> Order);

public interface IPvpMapDefinitionSource { PvpMapCatalog Load(); }

public interface IPvpRoomRuleService
{
    /// <summary>Whether a room may be set to this map index.</summary>
    bool AcceptsMap(int index);

    /// <summary>
    /// The map index a battle starts on: the room's own, or for index 0 a map drawn from the
    /// rest with <paramref name="seed"/>. Throws for an index <see cref="AcceptsMap"/> refuses.
    /// </summary>
    byte StartMap(int index, int seed);
}
