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

/// <summary>
/// Proves the three ways an inbound Game frame reaches no handler all end the same way on the wire:
/// nothing is written back and the connection stays usable. Silent on the wire is not silent in the
/// log, and the second half of this test pins that distinction down.
/// </summary>
/// <remarks>
/// The wire proof is positional rather than a timeout. Each dropped frame is followed by an EXIT
/// frame whose exact response is known; reading that response byte for byte would fail if any
/// earlier frame had emitted anything at all, because the emitted bytes would arrive first. The same
/// ordering makes the log assertions safe to run at the end: the third EXIT response cannot have
/// been read until all three frames were already processed.
/// </remarks>
public sealed class UnknownInboundFrameIntegrationTests
{
    [Fact]
    public async Task FramesWithNoDecoder_AreDroppedSilentlyAndLeaveTheConnectionUsable()
    {
        var logs = new RecordingLoggerProvider();
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
        builder.Logging.AddProvider(logs);
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

        // An unregistered wire key names itself and dumps its body: recovering the body of a
        // protocol this server does not implement yet is the entire reason the log line exists.
        var unregistered = logs.WarningsContaining("Unhandled Game frame");
        Assert.Equal(2, unregistered.Count);
        Assert.Contains(unregistered, entry => entry.Contains("selector=0x01") && entry.Contains("type=0x7FFF"));
        Assert.Contains(unregistered, entry => entry.Contains("selector=0x02") && entry.Contains("type=0x0003"));

        // A decoder that declines is a server-side defect, not traffic to study. It names the
        // decoder so the defect is locatable, and withholds the body so a decode failure on a
        // credential-carrying protocol cannot turn the log into a credential dump.
        var declined = Assert.Single(logs.WarningsContaining("declined"));
        Assert.Contains("GetUserInfoMessageDecoder", declined);
        Assert.Contains("type=0x0008", declined);
        Assert.Contains("body withheld", declined);
        Assert.DoesNotContain("FFFF01", declined);

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

    /// <remarks>
    /// Entries arrive on the socket receive thread rather than the test thread, so the list is
    /// guarded. Reads happen after the last response has been read back, which orders them behind
    /// every write this test causes.
    /// </remarks>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly Lock gate = new();
        private readonly List<(LogLevel Level, string Message)> entries = [];

        public IReadOnlyList<string> WarningsContaining(string fragment)
        {
            lock (gate)
            {
                return entries
                    .Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(fragment))
                    .Select(entry => entry.Message)
                    .ToArray();
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void Dispose()
        {
        }

        private void Add(LogLevel level, string message)
        {
            lock (gate)
            {
                entries.Add((level, message));
            }
        }

        private sealed class RecordingLogger(RecordingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                owner.Add(logLevel, formatter(state, exception));
        }
    }
}
