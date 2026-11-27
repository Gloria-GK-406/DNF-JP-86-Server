using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.World;

internal enum GuildTeleportEligibility { Locked, NeedsQuestLog, Allowed }

internal sealed class GuildTravelRuleService(ITownDefinitionSource definitions, IEntryPlacementRuleService placement)
{
    public static GuildTeleportEligibility EvaluateTeleportEligibility(
        TownAreaDefinition area, int? level, IReadOnlyList<ClearedQuest>? cleared = null)
    {
        if (level is null || level < area.MinimumLevel)
            return GuildTeleportEligibility.Locked;
        if (area.RequiredQuestIds.Count == 0)
            return GuildTeleportEligibility.Allowed;
        if (cleared is null)
            return GuildTeleportEligibility.NeedsQuestLog;
        return area.RequiredQuestIds.All(id => cleared.Any(quest => quest.QuestId == id && quest.Mark != 0))
            ? GuildTeleportEligibility.Allowed : GuildTeleportEligibility.Locked;
    }

    public async ValueTask<TownCrossingRefusal?> RefuseHallEntryAsync(CharacterPositionState source, byte destination,
        int? guildInstance, CancellationToken ct)
    {
        if (guildInstance is null || destination != GuildHallTown.InitialAreaId)
            return TownCrossingRefusal.GuildHallEntry;
        var town = await definitions.GetAsync(source.TownId, ct).ConfigureAwait(false);
        return town?.AreaAt(source.AreaId)?.Kind == TownAreaKind.Gate ? null : TownCrossingRefusal.GuildHallEntry;
    }

    public async ValueTask<TownCrossingRefusal?> RefuseHallCrossingAsync(byte source, byte destination, bool hasCurrentVisit,
        CancellationToken ct)
    {
        if (!hasCurrentVisit)
            return TownCrossingRefusal.GuildHallEntry;
        if (source is GuildHallTown.InitialAreaId or GuildHallTown.DungeonGateAreaId
            && destination is GuildHallTown.InitialAreaId or GuildHallTown.DungeonGateAreaId)
            return null;
        return await IsGuildBattleCrossingAsync(source, destination, ct).ConfigureAwait(false)
            ? null : TownCrossingRefusal.GuildHallEntry;
    }

    public static TownCrossingRefusal? RefuseHallExit(CharacterPositionState? home, byte town, byte area) =>
        home is not null && home.TownId == town && home.AreaId == area ? null : TownCrossingRefusal.GuildHallExit;

    private async ValueTask<bool> IsGuildBattleCrossingAsync(byte source, byte destination, CancellationToken ct)
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
        GuildPartyTeleportTarget target, ChannelEntryIdentity channel, int currentChannelId, CancellationToken ct)
    {
        if (channel.Kind != ChannelEntryKind.General || channel.ChannelId != currentChannelId
            || placement.IsHouseholdTown(target.TownId) || GuildHallTown.Is(target.TownId)
            || !await placement.CanEnterTownAsync(channel, target.TownId, ct).ConfigureAwait(false)
            || !await placement.SharesTownPresenceAsync(target.TownId, target.AreaId, ct).ConfigureAwait(false))
            return null;

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
