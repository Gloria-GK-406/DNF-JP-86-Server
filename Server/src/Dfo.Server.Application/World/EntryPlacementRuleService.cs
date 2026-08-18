using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// Decides where a character stands on entry, and what the channel permits.
/// </summary>
/// <remarks>
/// Reverse-engineered from the legacy channel spawn policy. Ordinary channels put a
/// character at their town's arrival gate and remember where they end up. The restricted
/// channel is different in kind: it serves one town, places arrivals next to that town's
/// local exit, and does not write the position back, so leaving returns them to wherever
/// they were.
/// </remarks>
internal sealed class EntryPlacementRuleService(
    ITownDefinitionSource towns,
    ChannelEntryOptions options) : IEntryPlacementRuleService
{
    /// <summary>The facing and area state the legacy server assigns to every arrival.</summary>
    private const byte ArrivalDirection = 5;

    private const byte ArrivalAreaState = 3;

    /// <summary>
    /// How far outside an exit an arrival is placed, so the character does not spawn on top
    /// of the thing they just came out of and immediately re-enter it.
    /// </summary>
    private const int ExitClearance = 8;

    public async ValueTask<ChannelEntryPolicyState> ResolveAsync(
        ChannelEntryIdentity channel,
        int persistedTownId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var spawn = channel.IsTransient
            ? await ResolveTransientSpawnAsync(cancellationToken).ConfigureAwait(false)
            : await ResolveTownGateSpawnAsync(persistedTownId, cancellationToken).ConfigureAwait(false);

        return new ChannelEntryPolicyState(
            channel.ChannelId,
            spawn,
            PersistsPosition: !channel.IsTransient,
            PvpSkillProfileApplies: channel.IsFreeDuel);
    }

    public ValueTask<bool> CanEnterTownAsync(
        ChannelEntryIdentity channel,
        int townId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        cancellationToken.ThrowIfCancellationRequested();

        // Only the restricted channel narrows where a character may go, and it narrows it
        // to the single town it serves.
        return ValueTask.FromResult(!channel.IsTransient || townId == options.LevelRestrictedTownId);
    }

    public async ValueTask<bool> DeclaresAreaAsync(
        int townId,
        int areaId,
        CancellationToken cancellationToken)
    {
        if (townId <= 0 || areaId < 0)
            return false;

        var town = await towns.GetAsync(townId, cancellationToken).ConfigureAwait(false);
        return town?.AreaAt(areaId) is not null;
    }

    private async ValueTask<EntrySpawnState> ResolveTownGateSpawnAsync(
        int persistedTownId,
        CancellationToken cancellationToken)
    {
        var townId = persistedTownId > 0 ? persistedTownId : options.DefaultTownId;
        var town = await towns.GetAsync(townId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntryPlacementUnavailableException(
                $"The content defines no town {townId}, so a character cannot be placed there.");

        var gate = town.Gate
            ?? throw new EntryPlacementUnavailableException(
                $"Town {townId} declares no arrival gate, so a character cannot be placed there.");

        return new EntrySpawnState(
            checked((byte)townId),
            checked((byte)gate.AreaId),
            checked((short)gate.Operand1),
            checked((short)gate.Operand2),
            ArrivalDirection,
            ArrivalAreaState,
            IsTransient: false);
    }

    private async ValueTask<EntrySpawnState> ResolveTransientSpawnAsync(CancellationToken cancellationToken)
    {
        var townId = options.LevelRestrictedTownId;
        var areaId = options.LevelRestrictedSpawnAreaId;

        var town = await towns.GetAsync(townId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntryPlacementUnavailableException(
                $"The content defines no town {townId}, which the restricted channel requires.");

        var area = town.AreaAt(areaId)
            ?? throw new EntryPlacementUnavailableException(
                $"Town {townId} declares no area {areaId}, which the restricted channel requires.");

        // The arrival is computed from the local exit's geometry, which only an area that
        // leads out of town has. Reading any other area's map would still yield a
        // coordinate, and it would be somewhere the character does not belong.
        if (area.Kind is not TownAreaKind.DungeonGate)
        {
            throw new EntryPlacementUnavailableException(
                $"Town {townId} area {areaId} is {area.Kind} rather than an area leading out of "
                + "town, so it states no arrival point for the restricted channel.");
        }

        var geometry = await towns.GetMapGeometryAsync(area.MapPath, cancellationToken).ConfigureAwait(false)
            ?? throw new EntryPlacementUnavailableException(
                $"The content defines no map '{area.MapPath}' for town {townId} area {areaId}.");

        if (!TryFindArrivalBesideLocalExit(geometry, out var x, out var y))
        {
            throw new EntryPlacementUnavailableException(
                $"Map '{area.MapPath}' offers no walkable arrival point beside a local exit.");
        }

        return new EntrySpawnState(
            checked((byte)townId),
            checked((byte)areaId),
            x,
            y,
            ArrivalDirection,
            ArrivalAreaState,
            IsTransient: true);
    }

    /// <summary>
    /// Finds a walkable point just outside a local exit, nearest to that exit.
    /// </summary>
    /// <remarks>
    /// Only exits that lead nowhere in town are considered. The others join two town maps,
    /// and arriving beside one of those would put the character next to a door back to a
    /// different map rather than at the exit they are meant to be standing by.
    /// <para>
    /// Each candidate exit contributes four points, one clear of each side. Points outside
    /// the walkable area are discarded, and the nearest survivor to its exit's centre wins.
    /// </para>
    /// </remarks>
    private static bool TryFindArrivalBesideLocalExit(MapGeometry geometry, out short x, out short y)
    {
        x = 0;
        y = 0;

        var bestDistance = long.MaxValue;
        var bestX = 0;
        var bestY = 0;
        var found = false;

        foreach (var exit in geometry.TransitAreas)
        {
            if (!exit.IsLocal)
                continue;

            var bounds = exit.Bounds;
            var centerX = bounds.CenterX;
            var centerY = bounds.CenterY;
            ReadOnlySpan<(int X, int Y)> candidates =
            [
                (bounds.X - ExitClearance, centerY),
                (bounds.X + Math.Max(0, bounds.Width) + ExitClearance, centerY),
                (centerX, bounds.Y - ExitClearance),
                (centerX, bounds.Y + Math.Max(0, bounds.Height) + ExitClearance),
            ];

            foreach (var (candidateX, candidateY) in candidates)
            {
                if (!geometry.IsWalkable(candidateX, candidateY))
                    continue;

                long deltaX = candidateX - centerX;
                long deltaY = candidateY - centerY;
                var distance = (deltaX * deltaX) + (deltaY * deltaY);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestX = candidateX;
                bestY = candidateY;
                found = true;
            }
        }

        if (!found || bestX is < short.MinValue or > short.MaxValue
                   || bestY is < short.MinValue or > short.MaxValue)
        {
            return false;
        }

        x = (short)bestX;
        y = (short)bestY;
        return true;
    }
}
