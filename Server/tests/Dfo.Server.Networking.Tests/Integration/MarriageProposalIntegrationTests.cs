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

public sealed class MarriageProposalIntegrationTests
{
    [Fact]
    public async Task Both_registered_requests_without_selection_return_typed_status_and_leave_socket_usable()
    {
        var channel = Port(); var game = Port(); while (game == channel) game = Port();
        var builder = Host.CreateApplicationBuilder(["--hostBuilder:reloadConfigOnChange=false"]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Networking:Enabled"] = "true", ["Networking:ChannelPort"] = channel.ToString(),
            ["Networking:GamePorts:0"] = game.ToString(), ["Game:EndpointHost"] = "127.0.0.1",
        });
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IServerConfigService>(new ServerConfigService(builder.Configuration));
        builder.Services.AddSingleton<IChannelCatalog>(StaticChannelCatalog.ForGamePort(game));
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
            await client.ConnectAsync(IPAddress.Loopback, game, timeout.Token);
            var stream = client.GetStream(); _ = await Read(stream, timeout.Token);
            await stream.WriteAsync(Frame(0x03F0, NativeBody("47000000010000")), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 0x03F0, Convert.FromHexString("01FFFFFFFF47000000010000")), await Read(stream, timeout.Token));
            await stream.WriteAsync(Frame(0x03F1, NativeBody("0000000001000001")), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 0x03F1, Convert.FromHexString("01FDFFFFFF0000000001000001")), await Read(stream, timeout.Token));
            await stream.WriteAsync(Frame(0x03F0, NativeBody("47000000020002")), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 0x03F0, Convert.FromHexString("01FFFFFFFF47000000020000")), await Read(stream, timeout.Token));
            await stream.WriteAsync(Frame(0x03F1, NativeBody("0000000002000000")), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 0x03F1, Convert.FromHexString("01FDFFFFFF0000000002000000")), await Read(stream, timeout.Token));
            await stream.WriteAsync(Frame(0x03F3, NativeBody("00000000")), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 0x03F3, new byte[] { 0, 1 }), await Read(stream, timeout.Token));
            await stream.WriteAsync(Frame(0x03F7, NativeBody("000000000000000000")), timeout.Token);
            using (var entryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                Assert.Equal(GameServerFrameEncoder.Encode(1, 0x03F7, Convert.FromHexString("01F6FFFFFF")),
                    await Read(stream, entryTimeout.Token));
            await stream.WriteAsync(Frame(0x03F6, NativeBody("0100000000000000")), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 0x03F6, Convert.FromHexString("01FFFFFFFF")), await Read(stream, timeout.Token));
            await stream.WriteAsync(Frame(0x03F2, Convert.FromHexString("000000001027000000000000")), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 0x03F2, Convert.FromHexString("01FFFFFFFF")), await Read(stream, timeout.Token));
            await stream.WriteAsync(Frame(3, []), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 3, [1]), await Read(stream, timeout.Token));
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }
    private static byte[] NativeBody(string fields) => [.. Enumerable.Repeat((byte)0xA5, 14), .. Convert.FromHexString(fields)];
    private static byte[] Frame(ushort type, ReadOnlySpan<byte> body)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength + body.Length]; frame[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12), 1);
        body.CopyTo(frame.AsSpan(GameClientFrameRequestInfo.HeaderLength)); return frame;
    }
    private static async Task<byte[]> Read(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[GameServerFrameEncoder.HeaderLength]; await stream.ReadExactlyAsync(header, ct);
        var frame = new byte[checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3)))];
        header.CopyTo(frame, 0); await stream.ReadExactlyAsync(frame.AsMemory(header.Length), ct); return frame;
    }
    private static int Port() => TestTcpPorts.Allocate();
}
