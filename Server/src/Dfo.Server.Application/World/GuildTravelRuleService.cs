using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.World;

internal sealed class GuildTravelRuleService(ITownDefinitionSource definitions)
{
    public async ValueTask<bool> IsTownGateAsync(byte townId, byte areaId, CancellationToken cancellationToken) =>
        (await definitions.GetAsync(townId, cancellationToken).ConfigureAwait(false))?.AreaAt(areaId)?.Kind
            == TownAreaKind.Gate;

    public async ValueTask<bool> IsGuildBattleCrossingAsync(byte source, byte destination, CancellationToken ct)
    {
        if ((source, destination) is not ((GuildHallTown.InitialAreaId, GuildHallTown.BattleAreaId)
            or (GuildHallTown.BattleAreaId, GuildHallTown.InitialAreaId)
            or (GuildHallTown.BattleAreaId, GuildHallTown.BattleAreaId)))
            return false;
        var town = await definitions.GetAsync(GuildHallTown.TownId, ct).ConfigureAwait(false);
        if (town?.AreaAt(GuildHallTown.InitialAreaId) is not { } hall)
            return false;
        var map = await definitions.GetMapGeometryAsync(hall.MapPath, ct).ConfigureAwait(false);
        return map?.TransitAreas.Any(transit => transit.LinkedTownId == GuildHallTown.TownId
            && transit.LinkedAreaId == GuildHallTown.BattleAreaId) == true;
    }

    public async ValueTask<(TownAreaDefinition Area, short X, short Y)?> TeleportDestinationAsync(
        GuildPartyTeleportTarget target, CancellationToken ct)
    {
        var town = await definitions.GetAsync(target.TownId, ct).ConfigureAwait(false);
        if (town?.AreaAt(target.AreaId) is not { } area)
            return null;

        var geometry = await definitions.GetMapGeometryAsync(area.MapPath, ct).ConfigureAwait(false);
        if (geometry is null || geometry.WalkableAreas.Count == 0)
            return null;
        var (x, y) = geometry.IsWalkable(target.X, target.Y)
            ? ((int)target.X, (int)target.Y)
            : (geometry.WalkableAreas[0].CenterX, geometry.WalkableAreas[0].CenterY);
        if (x is < short.MinValue or > short.MaxValue || y is < short.MinValue or > short.MaxValue)
            return null;

        return (area, (short)x, (short)y);
    }
}
