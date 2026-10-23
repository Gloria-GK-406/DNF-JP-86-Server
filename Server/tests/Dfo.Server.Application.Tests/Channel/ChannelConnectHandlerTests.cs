using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Channel;
using Dfo.Server.Protocol.Channel.Connect;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Channel;

public sealed class ChannelConnectHandlerTests
{
    [Fact]
    public async Task HandleAsync_SendsTheDateBasedConnectKeyToTheSameConnection()
    {
        var sender = new RecordingSender();
        var keyProvider = new ChannelProtocolKeyProvider(
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(8))));
        var handler = new ChannelConnectHandler(sender, keyProvider);
        var connectionId = new NetworkConnectionId("channel-42");
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(connectionId, NetworkEndpointKind.Channel, 7001));

        await TestGameplay.RunAsync(async () => await handler.HandleAsync(
            context,
            new ChannelConnectRequest(ChannelMessageMetadata.Empty),
            CancellationToken.None));

        Assert.Equal(connectionId, sender.ConnectionId);
        var response = Assert.IsType<ChannelConnectResponse>(sender.Message);
        Assert.Equal("20260810000006", response.EncryptionKey);
    }

    /// <summary>
    /// The last message a real registration adapter registered, read from its outbox; every call goes to it (TASK-25).
    /// An explicit delivery fails where it is registered, with the exception the old double threw.
    /// </summary>
    private sealed class RecordingSender : INetworkMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public RecordingSender() => outbound.RefuseExplicit(
            () => new InvalidOperationException("This handler does not use explicit-recipient delivery."));

        public NetworkConnectionId ConnectionId => outbound.Entries.LastOrDefault(entry => !entry.IsClose)?.Connection.ConnectionId ?? default;

        public IServerNetworkMessage? Message => outbound.Messages.LastOrDefault();

        public ValueTask SendAsync(
            NetworkConnectionId connectionId,
            IServerNetworkMessage message,
            CancellationToken cancellationToken) =>
            outbound.Network.SendAsync(connectionId, message, cancellationToken);

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            ExplicitMessageDelivery delivery,
            CancellationToken cancellationToken) =>
            outbound.Network.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone(
                "Test/UTC+8",
                TimeSpan.FromHours(8),
                "Test/UTC+8",
                "Test/UTC+8");
    }
}
