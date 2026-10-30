using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.World;

/// <summary>Whether a character level falls in a dungeon's recommended band.</summary>
internal static class DungeonRecommendation
{
    /// <remarks>
    /// A declared <c>[recommended level]</c> pair is the band. Without one, the band is the
    /// dungeon's minimum and basis levels widened by five on each side.
    /// </remarks>
    internal static bool IsRecommended(DungeonDefinition dungeon, int level) => dungeon.RecommendedLevels.Count == 2
        ? level >= dungeon.RecommendedLevels[0] && level <= dungeon.RecommendedLevels[1]
        : level > 0 && dungeon.MinimumRequiredLevel > 0 && level + 5 >= Math.Min(dungeon.MinimumRequiredLevel, dungeon.BasisLevel)
            && level - 5 <= Math.Max(dungeon.MinimumRequiredLevel, dungeon.BasisLevel);
}
