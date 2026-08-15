using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Application;
using Dfo.Server.Networking.Framing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// Proves the three ways an inbound Game frame reaches no handler all end the same way: nothing is
/// written back and the connection stays usable.
/// </summary>
/// <remarks>
/// The proof is positional rather than a timeout. Each dropped frame is followed by an EXIT frame
/// whose exact response is known; reading that response byte for byte would fail if any earlier
/// frame had emitted anything at all, because the emitted bytes would arrive first.
/// </remarks>
public sealed class UnknownInboundFrameIntegrationTests
{
    [Fact]
    public async Task FramesWithNoDecoder_AreDroppedSilentlyAndLeaveTheConnectionUsable()
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
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IChannelCatalog>(StaticChannelCatalog.ForGamePort(gamePort));
        builder.Services.AddSingleton(ChannelEntryOptions.Legacy);
        new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
        new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);

        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
        var stream = client.GetStream();
        _ = await ReadResponseAsync(stream, timeout.Token);
        var exitRequest = EncodeClientFrame(command: 0x01, type: 0x0003, body: []);
        var exitResponse = GameServerFrameEncoder.Encode(0x01, 0x0003, [0x01]);

        // No decoder is registered for this type, so the lookup misses.
        await stream.WriteAsync(EncodeClientFrame(command: 0x01, type: 0x7FFF, body: []), timeout.Token);
        await stream.WriteAsync(exitRequest, timeout.Token);
        Assert.Equal(exitResponse, await ReadResponseAsync(stream, timeout.Token));

        // Every decoder registers under command 0x01, so another command cannot match a wire key.
        await stream.WriteAsync(EncodeClientFrame(command: 0x02, type: 0x0003, body: []), timeout.Token);
        await stream.WriteAsync(exitRequest, timeout.Token);
        Assert.Equal(exitResponse, await ReadResponseAsync(stream, timeout.Token));

        // GET_USERINFO has a decoder, but this mode byte is not the one that yields a message.
        await stream.WriteAsync(
            EncodeClientFrame(command: 0x01, type: 0x0008, body: [0xFF, 0xFF, 0x01]),
            timeout.Token);
        await stream.WriteAsync(exitRequest, timeout.Token);
        Assert.Equal(exitResponse, await ReadResponseAsync(stream, timeout.Token));

        await host.StopAsync(timeout.Token);
    }

    private static byte[] EncodeClientFrame(byte command, ushort type, ReadOnlySpan<byte> body)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength + body.Length];
        frame[0] = command;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(11), 1);
        body.CopyTo(frame.AsSpan(GameClientFrameRequestInfo.HeaderLength));
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

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
