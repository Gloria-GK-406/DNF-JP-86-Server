using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Networking;

public interface ICommandRegistrationAttempt;

public interface ICommandMessageOutbox
{
    ValueTask RegisterAsync(NetworkConnectionId connectionId, IServerNetworkMessage message, CancellationToken cancellationToken);
    ValueTask RegisterBatchAsync(NetworkConnectionContext expectedConnection, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken cancellationToken);
    ValueTask RegisterExplicitAsync(NetworkConnectionId requiredDirectConnectionId, ExplicitMessageDelivery delivery, CancellationToken cancellationToken);
    ValueTask RegisterBatchAsync(NetworkConnectionContext expectedConnection, IReadOnlyList<IServerNetworkMessage> messages, ICommandRegistrationAttempt attempt, CancellationToken cancellationToken) =>
        ValueTask.FromException(new NotSupportedException("This outbox does not support explicit registration attempts."));
    ValueTask RegisterExplicitAsync(NetworkConnectionId requiredDirectConnectionId, ExplicitMessageDelivery delivery, ICommandRegistrationAttempt attempt, CancellationToken cancellationToken) =>
        ValueTask.FromException(new NotSupportedException("This outbox does not support explicit registration attempts."));
}
