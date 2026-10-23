using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Exit;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Game;

public sealed class GameExitMessageHandlerTests
{
    [Fact]
    public async Task HandleAsync_AcknowledgesTheExitOnTheSameConnection()
    {
        var sender = new RecordingSender();
        var handler = new GameExitHandler(sender);
        var connectionId = new NetworkConnectionId("game-42");
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(connectionId, NetworkEndpointKind.Game, 10011));

        await TestGameplay.RunAsync(async () => await handler.HandleAsync(
            context,
            new GameExitRequest(GameMessageMetadata.Empty),
            CancellationToken.None));

        Assert.Equal(connectionId, sender.ConnectionId);
        Assert.Equal(new GameExitResponse(true), sender.Message);
    }

    /// <summary>
    /// The last CMD a real registration adapter registered, read from its outbox; every call goes to the real protocol
    /// sender over it (TASK-25). A NOTI or an explicit delivery fails where it is registered, with the exception the
    /// old double threw.
    /// </summary>
    private sealed class RecordingSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public RecordingSender()
        {
            outbound.RefuseExplicit(() => new InvalidOperationException("This handler does not use explicit-recipient delivery."));
            outbound.FailAny(message => outbound.ExplicitInFlight is null && message is IGameNotiMessage,
                () => new InvalidOperationException("Exit sends a CMD response."));
        }

        public NetworkConnectionId ConnectionId => outbound.Entries.LastOrDefault(entry => !entry.IsClose)?.Connection.ConnectionId ?? default;

        public IGameServerMessage? Message => (IGameServerMessage?)outbound.Messages.LastOrDefault();

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
    }
}
