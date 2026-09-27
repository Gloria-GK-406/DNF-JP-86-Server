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

public sealed class PackageOpeningIntegrationTests
{
    [Theory]
    [InlineData(0x0450, "00420048B398007A0047B398003C00", "0002")]
    [InlineData(0x00D0, "0042007A00", "0002")]
    [InlineData(0x00A0, "4200", "0000FFFFFFFFFFFFFFFF000000000000000000000000")]
    [InlineData(0x0207, "4200010300000000", "0002")]
    public async Task Opening_without_selection_and_truncated_requests_are_answered_without_disconnect(int opcode, string captured, string failure)
    {
        var channelPort = AvailablePort(); var gamePort = AvailablePort();
        while (gamePort == channelPort) gamePort = AvailablePort();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Networking:Enabled"] = "true", ["Networking:ChannelPort"] = channelPort.ToString(),
            ["Networking:GamePorts:0"] = gamePort.ToString(), ["Game:EndpointHost"] = "127.0.0.1",
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await host.StartAsync(timeout.Token);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
            var stream = client.GetStream(); _ = await ReadAsync(stream, timeout.Token);
            foreach (var body in new[] { Convert.FromHexString(captured), new byte[] { 0 } })
            {
                await stream.WriteAsync(Frame((ushort)opcode, body), timeout.Token);
                Assert.Equal(GameServerFrameEncoder.Encode(1, (ushort)opcode, Convert.FromHexString(failure)), await ReadAsync(stream, timeout.Token));
            }
            await stream.WriteAsync(Frame(3, []), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 3, [1]), await ReadAsync(stream, timeout.Token));
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    private static byte[] Frame(ushort type, ReadOnlySpan<byte> body)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength + body.Length]; frame[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12), 1);
        body.CopyTo(frame.AsSpan(GameClientFrameRequestInfo.HeaderLength)); return frame;
    }
    private static async Task<byte[]> ReadAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[GameServerFrameEncoder.HeaderLength]; await stream.ReadExactlyAsync(header, ct);
        var frame = new byte[checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3)))];
        header.CopyTo(frame, 0); await stream.ReadExactlyAsync(frame.AsMemory(header.Length), ct); return frame;
    }
    private static int AvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }
}
