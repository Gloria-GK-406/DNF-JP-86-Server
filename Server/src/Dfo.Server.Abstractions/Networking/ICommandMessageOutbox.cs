using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Networking;

public interface ICommandMessageOutbox
{
    ValueTask RegisterAsync(NetworkConnectionId connectionId, IServerNetworkMessage message, CancellationToken cancellationToken);
    ValueTask RegisterBatchAsync(NetworkConnectionContext expectedConnection, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken cancellationToken);
    ValueTask RegisterExplicitAsync(NetworkConnectionId requiredDirectConnectionId, ExplicitMessageDelivery delivery, CancellationToken cancellationToken);
}
