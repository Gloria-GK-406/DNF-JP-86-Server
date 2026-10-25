using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.EplpRechallenge;
using Dfo.Server.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class EplpRechallengeFrameTests
{
    [Fact]
    public async Task Native_check_reaches_a_tcp_client_through_the_production_sender()
    {
        var channelPort = AvailablePort();
        var gamePort = AvailablePort();
        while (gamePort == channelPort) gamePort = AvailablePort();
        var builder = Host.CreateApplicationBuilder(["--hostBuilder:reloadConfigOnChange=false"]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Networking:Enabled"] = "true",
            ["Networking:ChannelPort"] = channelPort.ToString(),
            ["Networking:GamePorts:0"] = gamePort.ToString(),
            ["Game:EndpointHost"] = "127.0.0.1",
        });
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IServerConfigService>(new ServerConfigService(builder.Configuration));
        builder.Services.AddSingleton<IChannelCatalog>(StaticChannelCatalog.ForGamePort(gamePort));
        builder.Services.AddSingleton(ChannelEntryOptions.Legacy);
        new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
        new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);
        new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);
        using var host = TestInfrastructure.Build(builder);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await host.StartAsync(timeout.Token);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
            var stream = client.GetStream();
            _ = await ReadFrameAsync(stream, timeout.Token);
            var game = host.Services.GetRequiredService<IGameTcpService>();
            while (!game.GetClients().Any()) await Task.Delay(10, timeout.Token);
            var serverClient = Assert.IsAssignableFrom<TouchSocket.Sockets.ITcpSessionClient>(game.GetClients().Single());
            // A send is registered by a job and delivered when the job ends (T-006).
            await TestGameplay.RunAsync(host.Services, async () =>
                await host.Services.GetRequiredService<IGameMessageSender>().SendNotiAsync(
                    new NetworkConnectionId($"Game:{serverClient.Id}"),
                    new EplpRechallengeNotification(), timeout.Token));

            Assert.Equal(GameServerFrameEncoder.Encode(0, 0x0105, new byte[] { 9 }),
                await ReadFrameAsync(stream, timeout.Token));
        }
        finally { await host.StopAsync(timeout.Token); }
    }

    [Fact]
    public void Registered_encoder_sends_a_noti_0105_with_the_candidate_ready_byte_and_no_cmd_envelope()
    {
        var services = new ServiceCollection().AddLogging();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registry = new ServerMessageEncoderRegistry(
            provider.GetServices<ServerMessageEncoderRegistration>()
                .Where(x => x.MessageType == typeof(EplpRechallengeNotification)), provider);
        var entry = registry.Find(typeof(EplpRechallengeNotification));

        Assert.NotNull(entry);
        Assert.Equal(NetworkEndpointKind.Game, entry.Endpoint);
        var frame = Assert.Single(entry.Encoder.Encode(new EplpRechallengeNotification()));
        Assert.Equal(GameServerFrameEncoder.Encode(0x00, 0x0105, new byte[] { 9 }), frame);
        Assert.Equal(16, frame.Length);
    }

    private static int AvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[GameServerFrameEncoder.HeaderLength];
        await stream.ReadExactlyAsync(header, ct);
        var frame = new byte[checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3)))];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(header.Length), ct);
        return frame;
    }
}
