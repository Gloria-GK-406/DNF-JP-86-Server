using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Exit;

namespace Dfo.Server.Application.Tests.Game;

public sealed class GameExitMessageHandlerTests
{
    [Fact]
    public async Task HandleAsync_AcknowledgesTheExitOnTheSameConnection()
    {
        var sender = new RecordingSender();
        var bus = new StubCommandBus();
        var handler = new GameExitHandler(sender, bus);
        var connectionId = new NetworkConnectionId("game-42");
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(connectionId, NetworkEndpointKind.Game, 10011));

        await handler.HandleAsync(
            context,
            new GameExitRequest(GameMessageMetadata.Empty),
            CancellationToken.None);

        Assert.IsType<AcknowledgeGameExitCommand>(bus.Command);
        Assert.Equal(connectionId, sender.ConnectionId);
        Assert.Equal(new GameExitResponse(true), sender.Message);
    }

    private sealed class StubCommandBus : ICommandBus
    {
        public ICommand? Command { get; private set; }

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Command = command;
            return ValueTask.FromResult((TResult)(object)new AcknowledgeGameExitResult(true));
        }
    }

    private sealed class RecordingSender : INetworkMessageSender
    {
        public NetworkConnectionId ConnectionId { get; private set; }

        public IServerNetworkMessage? Message { get; private set; }

        public ValueTask SendAsync(
            NetworkConnectionId connectionId,
            IServerNetworkMessage message,
            CancellationToken cancellationToken)
        {
            ConnectionId = connectionId;
            Message = message;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            ExplicitMessageDelivery delivery,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This handler does not use explicit-recipient delivery.");
    }
}
