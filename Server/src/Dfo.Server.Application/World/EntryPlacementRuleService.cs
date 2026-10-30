using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

/// <summary>
/// Decides where a character stands on entry, and what the channel permits.
/// </summary>
/// <remarks>
/// Reverse-engineered from the legacy channel spawn policy. Ordinary channels put a
/// character at their remembered inn's arrival gate. Their live town is saved separately. Saint Horn
/// and other special channels use their own arrival gates without overwriting the remembered town.
/// </remarks>
internal sealed class EntryPlacementRuleService(
    ITownDefinitionSource towns,
    ChannelEntryOptions options,
    ILogger<EntryPlacementRuleService>? logger = null) : IEntryPlacementRuleService
{
    /// <summary>The facing and area state the legacy server assigns to every arrival.</summary>
    private const byte ArrivalDirection = 5;

    private const byte ArrivalAreaState = 3;

    public async ValueTask<bool> CanRememberInnAsync(int townId, CancellationToken cancellationToken) =>
        !CoupleRoomTown.Is(townId) && !IsSpecialTown(townId)
        && await TryResolveTownGateSpawnAsync(townId, cancellationToken).ConfigureAwait(false) is not null;

    public async ValueTask<ChannelEntryPolicyState> ResolveAsync(
        ChannelEntryIdentity channel,
        int persistedTownId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var spawn = channel.IsTransient
            ? await ResolveTransientSpawnAsync(channel, cancellationToken).ConfigureAwait(false)
            : await ResolveTownGateSpawnAsync(persistedTownId, cancellationToken).ConfigureAwait(false);

        return new ChannelEntryPolicyState(
            channel.ChannelId,
            spawn,
            PersistsPosition: !channel.IsTransient,
            PvpSkillProfileApplies: channel.IsPvp);
    }

    public ValueTask<bool> CanEnterTownAsync(
        ChannelEntryIdentity channel,
        int townId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(channel.IsTransient
            ? townId == SpecialTownOf(channel)
            : !IsSpecialTown(townId));
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

    public async ValueTask<bool> SharesTownPresenceAsync(
        int townId,
        int areaId,
        CancellationToken cancellationToken)
    {
        if (CoupleRoomTown.Is(townId))
            return true;
        if (townId <= 0 || areaId < 0)
            return false;

        var town = await towns.GetAsync(townId, cancellationToken).ConfigureAwait(false);
        return SharesTownPresence(town?.AreaAt(areaId));
    }

    private async ValueTask<EntrySpawnState> ResolveTownGateSpawnAsync(
        int persistedTownId,
        CancellationToken cancellationToken)
    {
        // Historical saves may contain the special town; a general channel must not restore it.
        var townId = persistedTownId > 0 && !CoupleRoomTown.Is(persistedTownId) && !IsSpecialTown(persistedTownId)
            ? persistedTownId : options.DefaultTownId;
        var spawn = await TryResolveTownGateSpawnAsync(townId, cancellationToken).ConfigureAwait(false);
        if (spawn is not null)
            return spawn;

        if (townId != options.DefaultTownId)
        {
            logger?.LogWarning(
                "Town {TownId} has no usable login arrival gate; falling back to initial town {DefaultTownId}",
                townId, options.DefaultTownId);
            spawn = await TryResolveTownGateSpawnAsync(options.DefaultTownId, cancellationToken)
                .ConfigureAwait(false);
            if (spawn is not null)
                return spawn;
        }

        throw new EntryPlacementUnavailableException(
            $"Initial town {options.DefaultTownId} declares no usable arrival gate (requested town {townId}).");
    }

    private async ValueTask<EntrySpawnState?> TryResolveTownGateSpawnAsync(
        int townId, CancellationToken cancellationToken)
    {
        var gate = (await towns.GetAsync(townId, cancellationToken).ConfigureAwait(false))?.Gate;
        if (gate is null || townId is <= 0 or > byte.MaxValue
            || gate.AreaId is < 0 or > byte.MaxValue
            || gate.Operand1 is < short.MinValue or > short.MaxValue
            || gate.Operand2 is < short.MinValue or > short.MaxValue)
            return null;

        return new EntrySpawnState(
            checked((byte)townId),
            checked((byte)gate.AreaId),
            checked((short)gate.Operand1),
            checked((short)gate.Operand2),
            ArrivalDirection,
            ArrivalAreaState,
            IsTransient: false,
            SharesTownPresence: SharesTownPresence(gate));
    }

    private bool IsSpecialTown(int townId) =>
        townId == options.SaintHornTownId || townId == options.PvpTownId || townId == options.RaidTownId;

    private int SpecialTownOf(ChannelEntryIdentity channel) => channel.Kind switch
    {
        ChannelEntryKind.SaintHorn => options.SaintHornTownId,
        ChannelEntryKind.Raid => options.RaidTownId,
        ChannelEntryKind.FreeDuel or ChannelEntryKind.Pvp => options.PvpTownId,
        _ => throw new InvalidOperationException("Channel has no transient town."),
    };

    private async ValueTask<EntrySpawnState> ResolveTransientSpawnAsync(
        ChannelEntryIdentity channel, CancellationToken cancellationToken)
    {
        var townId = SpecialTownOf(channel);
        var spawn = await TryResolveTownGateSpawnAsync(townId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntryPlacementUnavailableException(
                $"Channel {channel.ChannelId} town {townId} declares no usable arrival gate.");
        return spawn with { IsTransient = true };
    }

    private static bool SharesTownPresence(TownAreaDefinition? area) =>
        area is not null && area.Kind is not TownAreaKind.Gate;
}
