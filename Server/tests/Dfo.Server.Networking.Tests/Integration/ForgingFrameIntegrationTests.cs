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
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class ForgingFrameIntegrationTests
{
    [Fact]
    public async Task Raw_A21_forging_frame_reaches_handler_and_rejects_without_session()
    {
        var logs = new TestTcpStartupLog();
        var channelPort = GetAvailablePort();
        var gamePort = GetAvailablePort();
        while (gamePort == channelPort) gamePort = GetAvailablePort();
        var builder = Host.CreateApplicationBuilder(["--hostBuilder:reloadConfigOnChange=false"]);
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddProvider(logs);
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

        using var host = TestInfrastructure.Build(builder);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        TestTcpStartup.AssertReady(host.Services, logs, gamePort);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
        var stream = client.GetStream();
        _ = await ReadResponseAsync(stream, timeout.Token);

        byte[] body = [0, 9, 0, 0xB8, 0x0B, 0, 0, 121, 0,
            4, 0, 0, 0, 0x41, 0x42, 0x43, 0x44, 0];
        await stream.WriteAsync(EncodeClientFrame(0x01B7, body, 7), timeout.Token);
        Assert.Equal(GameServerFrameEncoder.Encode(0x01, 0x01B7, [0, 0x04]),
            await ReadResponseAsync(stream, timeout.Token));
        await logs.WaitForAsync("FORGING no selected session", timeout.Token);
        await host.StopAsync(timeout.Token);
    }

    private static byte[] EncodeClientFrame(ushort type, ReadOnlySpan<byte> body,
        ushort sequence)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength + body.Length];
        frame[0] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12), sequence);
        body.CopyTo(frame.AsSpan(GameClientFrameRequestInfo.HeaderLength));
        return frame;
    }

    private static async Task<byte[]> ReadResponseAsync(NetworkStream stream,
        CancellationToken ct)
    {
        var header = new byte[GameServerFrameEncoder.HeaderLength];
        await stream.ReadExactlyAsync(header, ct);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3, 4));
        var frame = new byte[checked((int)length)];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(header.Length), ct);
        return frame;
    }

    private static int GetAvailablePort() => TestTcpPorts.Allocate();

}
