using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Application;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class CeraShopPurchaseIntegrationTests
{
    [Fact]
    public async Task A_captured_request_without_a_selected_character_gets_a_failure_and_the_connection_remains_usable()
    {
        var channelPort = AvailablePort();
        var gamePort = AvailablePort();
        while (gamePort == channelPort) gamePort = AvailablePort();
        var builder = Host.CreateApplicationBuilder();
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
        builder.Services.AddSingleton<IChannelScriptSource>(new NoChannelScript());
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
            _ = await ReadAsync(stream, timeout.Token);
            var captured = Convert.FromHexString("0000010000FFFFB0AD01000000000000000000");
            await stream.WriteAsync(Frame(0x0040, captured), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 0x0040, Convert.FromHexString("000100FFFFFFFFFFFFFFFF000000000000000000000000")), await ReadAsync(stream, timeout.Token));
            await stream.WriteAsync(Frame(0x0040, [0, 0, 2, 0]), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 0x0040, Convert.FromHexString("000100FFFFFFFFFFFFFFFF000000000000000000000000")), await ReadAsync(stream, timeout.Token));
            foreach (var ticketBody in new byte[][] { [], [1, 2] })
            {
                await stream.WriteAsync(Frame(0x0041, ticketBody), timeout.Token);
                Assert.Equal(GameServerFrameEncoder.Encode(1, 0x0041, [0, 1]), await ReadAsync(stream, timeout.Token));
                var unavailable = await ReadAsync(stream, timeout.Token);
                Assert.Equal(0, unavailable[0]);
                Assert.Equal(0x01f7, BinaryPrimitives.ReadUInt16LittleEndian(unavailable.AsSpan(1)));
                Assert.Equal(0, unavailable[GameServerFrameEncoder.HeaderLength]);
                var textLength = BinaryPrimitives.ReadInt32LittleEndian(unavailable.AsSpan(GameServerFrameEncoder.HeaderLength + 1));
                Assert.Equal("黑钻权益由服务器统一设置，无需购买。", System.Text.Encoding.UTF8.GetString(unavailable.AsSpan(GameServerFrameEncoder.HeaderLength + 5, textLength)));
                Assert.Equal(GameServerFrameEncoder.HeaderLength + 5 + textLength, unavailable.Length);
            }
            await stream.WriteAsync(Frame(3, []), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 3, [1]), await ReadAsync(stream, timeout.Token));
        }
        finally { await host.StopAsync(timeout.Token); }
    }

    private static byte[] Frame(ushort type, ReadOnlySpan<byte> body)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength + body.Length];
        frame[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12), 1);
        body.CopyTo(frame.AsSpan(GameClientFrameRequestInfo.HeaderLength));
        return frame;
    }
    private static async Task<byte[]> ReadAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[GameServerFrameEncoder.HeaderLength];
        await stream.ReadExactlyAsync(header, ct);
        var frame = new byte[checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3)))];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(header.Length), ct);
        return frame;
    }
    private static int AvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
