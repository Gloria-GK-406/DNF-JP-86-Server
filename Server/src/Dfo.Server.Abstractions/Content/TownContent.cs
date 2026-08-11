namespace Dfo.Server.Abstractions.Content;

/// <summary>What a town area is for, as the content classifies it.</summary>
public enum TownAreaKind
{
    /// <summary>The content named a kind this contract does not model.</summary>
    Unrecognized = 0,

    /// <summary>An ordinary walkable area.</summary>
    Normal,

    /// <summary>The area a character arrives in, which also states where they stand.</summary>
    Gate,

    /// <summary>An area that leads out of town.</summary>
    DungeonGate,
}

/// <summary>A rectangle in map coordinates.</summary>
public readonly record struct MapRectangle(int X, int Y, int Width, int Height)
{
    public int CenterX => X + Math.Max(0, Width) / 2;

    public int CenterY => Y + Math.Max(0, Height) / 2;

    /// <summary>True when the point lies on or inside the rectangle.</summary>
    public bool Contains(int x, int y) =>
        x >= X && x <= X + Math.Max(0, Width) && y >= Y && y <= Y + Math.Max(0, Height);
}

/// <summary>
/// A rectangle on a map that moves a character somewhere else.
/// </summary>
/// <param name="LinkedTownId">The town it leads to, or <see cref="Unlinked"/>.</param>
/// <param name="LinkedAreaId">The area it leads to, or <see cref="Unlinked"/>.</param>
public readonly record struct TownTransitArea(MapRectangle Bounds, int LinkedTownId, int LinkedAreaId)
{
    /// <summary>The value the content uses where no destination is stated.</summary>
    public const int Unlinked = -1;

    /// <summary>
    /// True when the transit leads nowhere in town.
    /// </summary>
    /// <remarks>
    /// The content leaves both destinations unset on the gate that opens the local
    /// out-of-town selection, which is what distinguishes it from the transits that merely
    /// join two town maps.
    /// </remarks>
    public bool IsLocal => LinkedTownId == Unlinked && LinkedAreaId == Unlinked;
}

/// <summary>
/// The parts of a map's geometry that entry needs: where a character can be moved from,
/// and where they are allowed to stand.
/// </summary>
public sealed record MapGeometry(
    IReadOnlyList<TownTransitArea> TransitAreas,
    IReadOnlyList<MapRectangle> WalkableAreas)
{
    /// <summary>True when the point lies inside any area the content marks walkable.</summary>
    public bool IsWalkable(int x, int y)
    {
        foreach (var area in WalkableAreas)
        {
            if (area.Contains(x, y))
                return true;
        }

        return false;
    }
}

/// <summary>
/// One area of a town.
/// </summary>
/// <param name="Operand1">
/// The first value the content places after the area kind. Its meaning depends on the
/// kind: a gate states a spawn X here, a dungeon gate states which dungeon it opens.
/// </param>
/// <param name="Operand2">
/// The second value, present only where the kind uses one. A gate states its spawn Y.
/// </param>
public sealed record TownAreaDefinition(
    int AreaId,
    string MapPath,
    TownAreaKind Kind,
    int Operand1,
    int Operand2)
{
    /// <summary>The value the content uses where an operand is absent.</summary>
    public const int NoOperand = -1;
}

/// <summary>A town as the content defines it.</summary>
/// <param name="Name">The town's display name, or empty when the content states none.</param>
public sealed record TownDefinition(int TownId, string Name, IReadOnlyList<TownAreaDefinition> Areas)
{
    /// <summary>The area a character arrives in, or null when the town declares none.</summary>
    public TownAreaDefinition? Gate
    {
        get
        {
            foreach (var area in Areas)
            {
                if (area.Kind is TownAreaKind.Gate)
                    return area;
            }

            return null;
        }
    }

    /// <summary>One area by identifier, or null when the town does not declare it.</summary>
    public TownAreaDefinition? AreaAt(int areaId)
    {
        foreach (var area in Areas)
        {
            if (area.AreaId == areaId)
                return area;
        }

        return null;
    }
}

/// <summary>Static town, area and map-geometry definitions.</summary>
public interface ITownDefinitionSource
{
    /// <summary>Every town the content defines, in list order.</summary>
    ValueTask<IReadOnlyList<TownDefinition>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>One town, or null when the content does not define it.</summary>
    ValueTask<TownDefinition?> GetAsync(int townId, CancellationToken cancellationToken);

    /// <summary>
    /// The geometry of one map, or null when the content does not define that map.
    /// </summary>
    /// <remarks>
    /// Geometry is fetched per map rather than carried on every area, because a town's
    /// areas each reference a map and only the one being entered is needed.
    /// </remarks>
    ValueTask<MapGeometry?> GetMapGeometryAsync(string mapPath, CancellationToken cancellationToken);
}
