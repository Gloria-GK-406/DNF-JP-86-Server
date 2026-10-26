using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

public interface IGameCommandOutbox
{
    ValueTask<bool> RegisterCmdWithOutcomeAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
        ValueTask.FromException<bool>(new NotSupportedException("This outbox does not report registration acceptance."));
    ValueTask<bool> RegisterNotiWithOutcomeAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
        ValueTask.FromException<bool>(new NotSupportedException("This outbox does not report registration acceptance."));
    ValueTask RegisterCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken);
    ValueTask RegisterNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken);
    ValueTask RegisterBatchAsync(NetworkConnectionContext expectedConnection, IReadOnlyList<IGameServerMessage> messages, CancellationToken cancellationToken);
    ValueTask RegisterExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken);
    ValueTask RegisterBatchAsync(NetworkConnectionContext expectedConnection, IReadOnlyList<IGameServerMessage> messages, ICommandRegistrationAttempt attempt, CancellationToken cancellationToken) =>
        ValueTask.FromException(new NotSupportedException("This outbox does not support explicit registration attempts."));
    ValueTask RegisterExplicitAsync(NetworkConnectionId connectionId, GameExplicitMessageDelivery delivery, ICommandRegistrationAttempt attempt, CancellationToken cancellationToken) =>
        ValueTask.FromException(new NotSupportedException("This outbox does not support explicit registration attempts."));
}

internal sealed class GameCommandOutbox(ICommandMessageOutbox outbox) : IGameCommandOutbox
{
    public ValueTask<bool> RegisterCmdWithOutcomeAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
        outbox.RegisterWithOutcomeAsync(connectionId, message, cancellationToken);
    public ValueTask<bool> RegisterNotiWithOutcomeAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
        outbox.RegisterWithOutcomeAsync(connectionId, message, cancellationToken);
    public ValueTask RegisterCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
        outbox.RegisterAsync(connectionId, message, cancellationToken);
    public ValueTask RegisterNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
        outbox.RegisterAsync(connectionId, message, cancellationToken);
    public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IGameServerMessage> messages, CancellationToken cancellationToken) =>
        outbox.RegisterBatchAsync(connection, [.. messages.Cast<IServerNetworkMessage>()], cancellationToken);
    public ValueTask RegisterExplicitAsync(NetworkConnectionId connectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
        outbox.RegisterExplicitAsync(connectionId, delivery.ToNetworkDelivery(), cancellationToken);
    public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IGameServerMessage> messages, ICommandRegistrationAttempt attempt, CancellationToken cancellationToken) =>
        outbox.RegisterBatchAsync(connection, [.. messages.Cast<IServerNetworkMessage>()], attempt, cancellationToken);
    public ValueTask RegisterExplicitAsync(NetworkConnectionId connectionId, GameExplicitMessageDelivery delivery, ICommandRegistrationAttempt attempt, CancellationToken cancellationToken) =>
        outbox.RegisterExplicitAsync(connectionId, delivery.ToNetworkDelivery(), attempt, cancellationToken);

}
