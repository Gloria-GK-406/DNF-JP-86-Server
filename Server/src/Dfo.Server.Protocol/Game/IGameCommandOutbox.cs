using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

public interface IGameCommandOutbox
{
    ValueTask RegisterCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken);
    ValueTask RegisterNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken);
    ValueTask RegisterBatchAsync(NetworkConnectionContext expectedConnection, IReadOnlyList<IGameServerMessage> messages, CancellationToken cancellationToken);
    ValueTask RegisterExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken);
}

internal sealed class GameCommandOutbox(ICommandMessageOutbox outbox) : IGameCommandOutbox
{
    public ValueTask RegisterCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
        outbox.RegisterAsync(connectionId, message, cancellationToken);
    public ValueTask RegisterNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
        outbox.RegisterAsync(connectionId, message, cancellationToken);
    public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IGameServerMessage> messages, CancellationToken cancellationToken) =>
        outbox.RegisterBatchAsync(connection, [.. messages.Cast<IServerNetworkMessage>()], cancellationToken);
    public ValueTask RegisterExplicitAsync(NetworkConnectionId connectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
        outbox.RegisterExplicitAsync(connectionId, delivery.ToNetworkDelivery(), cancellationToken);
}
