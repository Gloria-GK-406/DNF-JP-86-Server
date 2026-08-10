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
}

public interface INetworkConnectionObserver
{
    ValueTask ConnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken);

    ValueTask DisconnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken);
}
