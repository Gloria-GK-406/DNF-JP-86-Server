using System.Net;
using System.Net.Sockets;
using Dfo.Server.Application;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouchSocketTcpSession = TouchSocket.Sockets.ITcpSession;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class ChannelConnectIntegrationTests
{
    [Fact]
    public async Task ChannelConnect_TraversesTouchSocketDispatcherHandlerAndSender()
    {
        var channelPort = GetAvailablePort();
        var gamePort = GetAvailablePort();
        while (gamePort == channelPort)
        {
            gamePort = GetAvailablePort();
        }
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Networking:Enabled"] = "true",
            ["Networking:ChannelPort"] = channelPort.ToString(),
            ["Networking:GamePorts:0"] = gamePort.ToString()
        });
        builder.Services.AddSingleton<TimeProvider>(
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(8))));
        new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
        new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);

        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);

        var channelService = host.Services.GetRequiredService<IChannelTcpService>();
        var gameService = host.Services.GetRequiredService<IGameTcpService>();
        Assert.NotSame(channelService, gameService);
        Assert.IsType<ChannelTcpService>(channelService);
        Assert.IsType<GameTcpService>(gameService);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, channelPort, timeout.Token);
        using var secondChannelClient = new TcpClient();
        await secondChannelClient.ConnectAsync(IPAddress.Loopback, channelPort, timeout.Token);
        using var gameClient = new TcpClient();
        await gameClient.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
        await WaitUntilAsync(
            () => channelService.GetClients().Count() == 2 && gameService.GetClients().Count() == 1,
            timeout.Token);

        var channelAdapters = channelService.GetClients()
            .Cast<TouchSocketTcpSession>()
            .Select(session => session.DataHandlingAdapter)
            .ToArray();
        Assert.All(channelAdapters, adapter => Assert.IsType<ChannelFrameDataHandlingAdapter>(adapter));
        Assert.NotSame(channelAdapters[0], channelAdapters[1]);
        Assert.IsType<GameClientFrameDataHandlingAdapter>(
            gameService.GetClients().Cast<TouchSocketTcpSession>().Single().DataHandlingAdapter);

        var request = ChannelFrameEncoder.Encode(
            classification: 0x7C,
            messageNumber: 0x0B,
            checksum: 0,
            acknowledgement: 1,
            body: ReadOnlySpan<byte>.Empty);
        await client.GetStream().WriteAsync(request, timeout.Token);

        var response = new byte[47];
        await client.GetStream().ReadExactlyAsync(response, timeout.Token);

        var expectedBody = new byte[36];
        "20260810000006"u8.CopyTo(expectedBody.AsSpan(4));
        var expected = ChannelFrameEncoder.Encode(
            classification: 0x7C,
            messageNumber: 0x0C,
            checksum: 0,
            acknowledgement: 1,
            body: expectedBody);
        Assert.Equal(expected, response);

        await host.StopAsync(timeout.Token);
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("Test/UTC+8", TimeSpan.FromHours(8), "Test/UTC+8", "Test/UTC+8");
    }
}
