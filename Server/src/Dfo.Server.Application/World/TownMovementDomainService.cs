using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.World;

/// <summary>Whether a character may stand where they say they are standing.</summary>
internal sealed class TownMovementDomainService(
    IEntryPlacementRuleService placement) : ITownMovementDomainService
{
    public ValueTask<bool> DeclaresAreaAsync(
        int townId, int areaId, CancellationToken cancellationToken) =>
        placement.DeclaresAreaAsync(townId, areaId, cancellationToken);

    public ValueTask<bool> CanEnterTownAsync(
        ChannelEntryIdentity channel, int townId, CancellationToken cancellationToken) =>
        placement.CanEnterTownAsync(channel, townId, cancellationToken);

    public ValueTask<bool> SharesTownPresenceAsync(
        int townId,
        int areaId,
        CancellationToken cancellationToken) =>
        placement.SharesTownPresenceAsync(townId, areaId, cancellationToken);
}
