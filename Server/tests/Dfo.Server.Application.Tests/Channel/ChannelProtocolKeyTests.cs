using System.Globalization;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Channel;
using Dfo.Server.Protocol.Channel.Connect;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Channel;

public sealed class ChannelProtocolKeyTests
{
    [Theory]
    [InlineData("2026-08-10T04:00:00Z", 8, "en-US", "20260810000006")]
    [InlineData("2026-08-09T16:30:00Z", 8, "th-TH", "20260810000006")]
    [InlineData("2026-08-10T01:00:00Z", -8, "th-TH", "20260809000006")]
    public async Task Provider_and_connect_response_preserve_the_local_Gregorian_date_key(
        string utc, int offsetHours, string cultureName, string expected)
    {
        var clock = new FixedTimeProvider(DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture), offsetHours);
        var provider = new ChannelProtocolKeyProvider(clock);
        var outbound = new KeyOutbox();
        var handler = new ChannelConnectCommandHandler(clock, outbound);
        var connection = new NetworkConnectionContext(new("channel-42"), NetworkEndpointKind.Channel, 7001);
        var command = new ChannelConnectCommand(new(connection), new(ChannelMessageMetadata.Empty));
        await TestGameplay.RunAsync(async () =>
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                if (cultureName == "th-TH")
                    Assert.NotEqual("2026", clock.GetLocalNow().ToString("yyyy", CultureInfo.CurrentCulture));
                Assert.Equal(expected, provider.GetCurrentKey());
                await handler.HandleAsync(new(Guid.NewGuid(), command.GetType(), handler.GetType()),
                    command, CancellationToken.None);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });
        Assert.Equal(connection.ConnectionId, Assert.Single(outbound.Recorded.Entries).Connection.ConnectionId);
        var response = Assert.IsType<ChannelConnectResponse>(Assert.Single(outbound.Recorded.Messages));
        Assert.Equal(expected, response.EncryptionKey);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utc, int offsetHours) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utc.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone(
            "Test/Local", TimeSpan.FromHours(offsetHours), "Test/Local", "Test/Local");
    }

    private sealed class KeyOutbox : ICommandMessageOutbox
    {
        public RecordedOutbound Recorded { get; } = new();
        public ValueTask RegisterAsync(NetworkConnectionId connection, IServerNetworkMessage message, CancellationToken ct) =>
            Recorded.Network.SendAsync(connection, message, ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken ct) =>
            Recorded.Network.SendBatchAsync(connection, messages, ct);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId connection, ExplicitMessageDelivery delivery, CancellationToken ct) =>
            Recorded.Network.SendExplicitAsync(connection, delivery, ct);
    }
}
