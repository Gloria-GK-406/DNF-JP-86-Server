using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Channel;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Tests.Channel;

public sealed class ChannelConnectHandlerTests
{
    [Fact]
    public async Task HandleAsync_SendsTheDateBasedConnectKeyToTheSameConnection()
    {
        var sender = new RecordingSender();
        var handler = new ChannelConnectHandler(
            sender,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(8))));
        var connectionId = new NetworkConnectionId("channel-42");
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(connectionId, NetworkEndpointKind.Channel, 7001));

        await handler.HandleAsync(
            context,
            new ChannelConnectRequest(ChannelMessageMetadata.Empty),
            CancellationToken.None);

        Assert.Equal(connectionId, sender.ConnectionId);
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
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("Test/UTC+8", TimeSpan.FromHours(8), "Test/UTC+8", "Test/UTC+8");
    }
}
