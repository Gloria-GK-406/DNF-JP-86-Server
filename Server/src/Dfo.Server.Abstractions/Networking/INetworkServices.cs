using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Networking;

public interface INetworkMessageDispatcher
{
    ValueTask DispatchAsync(
        NetworkDispatchContext context,
        IClientNetworkMessage message,
        CancellationToken cancellationToken);
}

public interface INetworkMessageSender
{
    ValueTask SendAsync(
        NetworkConnectionId connectionId,
        IServerNetworkMessage message,
        CancellationToken cancellationToken);

    ValueTask SendProjectionAsync(
        NetworkConnectionId requiredDirectConnectionId,
        SelectCharacterProjectionDelivery delivery,
        CancellationToken cancellationToken) =>
        SendAsync(requiredDirectConnectionId, delivery, cancellationToken);
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
