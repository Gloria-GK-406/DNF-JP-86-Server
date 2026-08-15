using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Channel;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Tests.Channel;

public sealed class ChannelConnectHandlerTests
{
    [Fact]
    public async Task HandleAsync_SendsTheDateBasedConnectKeyToTheSameConnection()
    {
        var sender = new RecordingSender();
        var commandBus = new RecordingCommandBus(
            new EstablishChannelConnectionResult("20260810000006"));
        var handler = new ChannelConnectHandler(sender, commandBus);
        var connectionId = new NetworkConnectionId("channel-42");
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(connectionId, NetworkEndpointKind.Channel, 7001));

        await handler.HandleAsync(
            context,
            new ChannelConnectRequest(ChannelMessageMetadata.Empty),
            CancellationToken.None);

        Assert.Equal(connectionId, sender.ConnectionId);
        Assert.IsType<EstablishChannelConnectionCommand>(commandBus.Command);
        var response = Assert.IsType<ChannelConnectResponse>(sender.Message);
        Assert.Equal("20260810000006", response.EncryptionKey);
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

        public ValueTask SendProjectionAsync(
            NetworkConnectionId requiredDirectConnectionId,
            SelectCharacterProjectionDelivery delivery,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This handler does not deliver projections.");
    }

    private sealed class RecordingCommandBus(EstablishChannelConnectionResult result) : ICommandBus
    {
        public ICommand? Command { get; private set; }

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Command = command;
            return ValueTask.FromResult((TResult)(object)result);
        }
    }
}
