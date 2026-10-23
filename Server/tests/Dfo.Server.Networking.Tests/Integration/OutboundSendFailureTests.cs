using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Protocol.Channel.Connect;
using Dfo.Server.Protocol.Game.Exit;
using Dfo.Server.Protocol.Game.Login;
using Dfo.Server.Protocol;
using Dfo.Server.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// Exercises the outbound rules that no successful send can reach: sending to the wrong endpoint,
/// sending a message nothing encodes, and encoding the login failure body that has never been
/// observed on the wire.
/// </summary>
/// <remarks>
/// Driven through a real host so the real registry, the real registrations and the real sender are
/// the things under test. Connection identities are captured through the ordinary connection
/// observer seam rather than reached into.
/// </remarks>
public sealed class OutboundSendFailureTests
{
    [Fact]
    public async Task AGameMessageIsRejectedOnAChannelConnection()
    {
        await using var fixture = await OutboundFixture.StartAsync();
        var channelConnection = await fixture.ConnectChannelAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await TestGameplay.RunAsync(fixture.Services, async () => await fixture.Sender.SendAsync(
                channelConnection, new GameExitResponse(true), fixture.Token)));

        Assert.Contains(nameof(GameExitResponse), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(NetworkEndpointKind.Channel), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChannelMessageIsRejectedOnAGameConnection()
    {
        await using var fixture = await OutboundFixture.StartAsync();
        var gameConnection = await fixture.ConnectGameAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await TestGameplay.RunAsync(fixture.Services, async () => await fixture.Sender.SendAsync(
                gameConnection, new ChannelConnectResponse("key"), fixture.Token)));

        Assert.Contains(nameof(ChannelConnectResponse), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(NetworkEndpointKind.Game), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMessageWithNoRegisteredEncoderIsRejectedByName()
    {
        await using var fixture = await OutboundFixture.StartAsync();
        var gameConnection = await fixture.ConnectGameAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await TestGameplay.RunAsync(fixture.Services, async () => await fixture.Sender.SendAsync(
                gameConnection, new UnregisteredMessage(), fixture.Token)));

        Assert.Contains(nameof(UnregisteredMessage), exception.Message, StringComparison.Ordinal);
        Assert.Contains("encoder", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The failure body has never been observed, so it must stay unencodable.</summary>
    [Fact]
    public async Task AFailedLoginResponseIsNotEncodable()
    {
        await using var fixture = await OutboundFixture.StartAsync();
        var gameConnection = await fixture.ConnectGameAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await TestGameplay.RunAsync(fixture.Services, async () => await fixture.Sender.SendAsync(
                gameConnection,
                new GameLoginResponse(false, 1, 10128, "192.0.2.20", 17200, 17200),
                fixture.Token)));

        Assert.Contains("failure body", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record UnregisteredMessage : IServerNetworkMessage
    {
        public ServerMessageType MessageType => ServerMessageType.GameExit;
    }

    private sealed class OutboundFixture : IAsyncDisposable
    {
        private readonly IHost host;
        private readonly CancellationTokenSource timeout;
        private readonly ConnectionCapture capture;
        private readonly List<TcpClient> clients = [];

        private OutboundFixture(IHost host, CancellationTokenSource timeout, ConnectionCapture capture)
        {
            this.host = host;
            this.timeout = timeout;
            this.capture = capture;
            ChannelPort = capture.ChannelPort;
            GamePort = capture.GamePort;
        }

        public int ChannelPort { get; }

        public int GamePort { get; }

        public CancellationToken Token => timeout.Token;

        public INetworkMessageSender Sender => host.Services.GetRequiredService<INetworkMessageSender>();

        public IServiceProvider Services => host.Services;

        public static async Task<OutboundFixture> StartAsync()
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
                ["Networking:GamePorts:0"] = gamePort.ToString(),
                ["Game:EndpointHost"] = "127.0.0.1",
            });
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddSingleton<IServerConfigService>(
                new ServerConfigService(builder.Configuration));
            builder.Services.AddSingleton<IChannelCatalog>(StaticChannelCatalog.ForGamePort(gamePort));
            builder.Services.AddSingleton<IChannelScriptSource>(new NoChannelScript());
            builder.Services.AddSingleton(ChannelEntryOptions.Legacy);
            new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
            new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);
            new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);
            var capture = new ConnectionCapture(channelPort, gamePort);
            builder.Services.AddSingleton<INetworkConnectionObserver>(capture);

            var host = TestInfrastructure.Build(builder);
            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StartAsync(timeout.Token);
            return new OutboundFixture(host, timeout, capture);
        }

        public Task<NetworkConnectionId> ConnectChannelAsync() =>
            ConnectAsync(ChannelPort, NetworkEndpointKind.Channel);

        public Task<NetworkConnectionId> ConnectGameAsync() =>
            ConnectAsync(GamePort, NetworkEndpointKind.Game);

        private async Task<NetworkConnectionId> ConnectAsync(int port, NetworkEndpointKind kind)
        {
            var client = new TcpClient();
            clients.Add(client);
            await client.ConnectAsync(IPAddress.Loopback, port, Token);
            return await capture.WaitForAsync(kind, Token);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }

            await host.StopAsync(CancellationToken.None);
            host.Dispose();
            timeout.Dispose();
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed class ConnectionCapture(int channelPort, int gamePort) : INetworkConnectionObserver
    {
        private readonly Dictionary<NetworkEndpointKind, NetworkConnectionId> connected = [];
        private readonly SemaphoreSlim signal = new(0);

        public int ChannelPort { get; } = channelPort;

        public int GamePort { get; } = gamePort;

        public ValueTask ConnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken)
        {
            lock (connected)
            {
                connected[connection.EndpointKind] = connection.ConnectionId;
            }

            signal.Release();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public async Task<NetworkConnectionId> WaitForAsync(
            NetworkEndpointKind kind,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                lock (connected)
                {
                    if (connected.TryGetValue(kind, out var id))
                    {
                        return id;
                    }
                }

                await signal.WaitAsync(cancellationToken);
            }
        }
    }
}
