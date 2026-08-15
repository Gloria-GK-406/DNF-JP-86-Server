using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Networking;

public interface INetworkMessageDispatcher
{
    ValueTask DispatchAsync(
        NetworkDispatchContext context,
        IClientNetworkMessage message,
        CancellationToken cancellationToken);
}

/// <summary>An explicitly addressed best-effort projection batch for one connection.</summary>
public sealed record ExplicitProjectionTarget(
    NetworkConnectionId ConnectionId,
    IReadOnlyList<Dfo.Server.Data.Game.ISelectCharacterSupplementalProjection> OrderedProjections);

/// <summary>
/// Typed Select Character projections. Networking frames them; required direct values are delivered first;
/// targets are already selected by the caller and are attempted best-effort afterwards.
/// </summary>
/// <remarks>
/// This is a delivery plan, not a message. It carries no wire fields -- its payload is domain
/// projections -- and unlike every message it names its own recipients. It therefore lives beside
/// the seam that consumes it rather than among the message contracts, and it deliberately does not
/// implement <see cref="IServerNetworkMessage"/>: there is no encoder that could turn a fan-out
/// plan into one frame, so passing it to <see cref="INetworkMessageSender.SendAsync"/> must fail to
/// compile rather than fail at runtime.
/// </remarks>
public sealed record SelectCharacterProjectionDelivery(
    IReadOnlyList<Dfo.Server.Data.Game.ISelectCharacterSupplementalProjection> RequiredDirectProjections,
    IReadOnlyList<ExplicitProjectionTarget> BestEffortTargets);

public interface INetworkMessageSender
{
    ValueTask SendAsync(
        NetworkConnectionId connectionId,
        IServerNetworkMessage message,
        CancellationToken cancellationToken);

    ValueTask SendProjectionAsync(
        NetworkConnectionId requiredDirectConnectionId,
        SelectCharacterProjectionDelivery delivery,
        CancellationToken cancellationToken);
}

public interface INetworkConnectionCloser
{
    ValueTask CloseAsync(NetworkConnectionId connectionId, CancellationToken cancellationToken);
}

/// <summary>Networking-owned framing boundary for ordered typed post-selection projections.</summary>
public interface ISelectCharacterProjectionComposer
{
    IReadOnlyList<byte[]> Compose(
        IReadOnlyList<Dfo.Server.Data.Game.ISelectCharacterSupplementalProjection> projections);
}

public interface INetworkConnectionObserver
{
    ValueTask ConnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken);

    ValueTask DisconnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken);
}
