using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Networking;

public interface ICommandRegistrationAttempt;

public interface ICommandMessageOutbox
{
    ValueTask<bool> RegisterWithOutcomeAsync(NetworkConnectionId connectionId, IServerNetworkMessage message, CancellationToken cancellationToken) =>
        ValueTask.FromException<bool>(new NotSupportedException("This outbox does not report registration acceptance."));
    ValueTask RegisterAsync(NetworkConnectionId connectionId, IServerNetworkMessage message, CancellationToken cancellationToken);
    ValueTask<bool> RegisterBatchWithOutcomeAsync(NetworkConnectionContext expectedConnection, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken cancellationToken) =>
        ValueTask.FromException<bool>(new NotSupportedException("This outbox does not report batch acceptance."));
    ValueTask RegisterBatchAsync(NetworkConnectionContext expectedConnection, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken cancellationToken);
    ValueTask RegisterExplicitAsync(NetworkConnectionId requiredDirectConnectionId, ExplicitMessageDelivery delivery, CancellationToken cancellationToken);
    ValueTask RegisterBatchAsync(NetworkConnectionContext expectedConnection, IReadOnlyList<IServerNetworkMessage> messages, ICommandRegistrationAttempt attempt, CancellationToken cancellationToken) =>
        ValueTask.FromException(new NotSupportedException("This outbox does not support explicit registration attempts."));
    ValueTask RegisterExplicitAsync(NetworkConnectionId requiredDirectConnectionId, ExplicitMessageDelivery delivery, ICommandRegistrationAttempt attempt, CancellationToken cancellationToken) =>
        ValueTask.FromException(new NotSupportedException("This outbox does not support explicit registration attempts."));
}
