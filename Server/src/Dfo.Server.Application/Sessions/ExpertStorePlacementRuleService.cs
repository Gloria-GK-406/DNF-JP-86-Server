using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Sessions;

internal sealed class ExpertStorePlacementRuleService(ITownDefinitionSource towns)
{
    internal async ValueTask<ExpertStoreRejection> CheckAsync(TownAreaKey key, short x, short y, CancellationToken ct)
    {
        var town = await towns.GetAsync(key.TownId, ct).ConfigureAwait(false);
        var area = town?.AreaAt(key.AreaId);
        if (area is null || await towns.GetMapGeometryAsync(area.MapPath, ct).ConfigureAwait(false) is not { } geometry
            || geometry.NpcPositions is not { } npcPositions || !geometry.IsWalkable(x, y))
            return ExpertStoreRejection.UnavailablePoint;
        return npcPositions.Any(npc => Math.Abs((long)npc.X - x) < 80 && Math.Abs((long)npc.Y - y) < 150)
            ? ExpertStoreRejection.RestrictedCommercialZone
            : ExpertStoreRejection.None;
    }
}
