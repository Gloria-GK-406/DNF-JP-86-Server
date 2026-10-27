using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Application;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class GameExitIntegrationTests
{
    [Fact]
    public async Task Exit_TraversesTypedPipelineAndAcknowledgesWithoutClosingConnection()
    {
        var channelPort = GetAvailablePort();
        var gamePort = GetAvailablePort();
        while (gamePort == channelPort)
        {
            gamePort = GetAvailablePort();
        }

        var builder = Host.CreateApplicationBuilder(["--hostBuilder:reloadConfigOnChange=false"]);
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

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
        var stream = client.GetStream();
        var initialNotice = await ReadResponseAsync(stream, timeout.Token);
        Assert.Equal(0x00, initialNotice[0]);
        Assert.Equal(0x0001, BinaryPrimitives.ReadUInt16LittleEndian(initialNotice.AsSpan(1, 2)));
        var request = EncodeClientFrame(command: 0x01, type: 0x0003, checksum: 0, sequence: 7);
        var expected = GameServerFrameEncoder.Encode(0x01, 0x0003, [0x01]);

        await stream.WriteAsync(request, timeout.Token);
        Assert.Equal(expected, await ReadResponseAsync(stream, timeout.Token));

        await stream.WriteAsync(request, timeout.Token);
        Assert.Equal(expected, await ReadResponseAsync(stream, timeout.Token));

        await host.StopAsync(timeout.Token);
    }

    private static byte[] EncodeClientFrame(byte command, ushort type, uint checksum, ushort sequence)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength];
        frame[0] = command;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7), checksum);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(11), sequence);
        return frame;
    }

    private static async Task<byte[]> ReadResponseAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[GameServerFrameEncoder.HeaderLength];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var totalLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3, 4));
        var frame = new byte[checked((int)totalLength)];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(header.Length), cancellationToken);
        return frame;
    }

    private static int GetAvailablePort() => TestTcpPorts.Allocate();
}
