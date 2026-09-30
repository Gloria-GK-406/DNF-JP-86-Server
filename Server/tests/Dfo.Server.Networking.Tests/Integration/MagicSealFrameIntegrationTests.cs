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

public sealed class MagicSealFrameIntegrationTests
{
    [Fact]
    public async Task Raw_A21_frames_reach_both_registered_handlers_and_change_rejection_is_encoded()
    {
        var logs = new CaptureLoggerProvider();
        var channelPort = GetAvailablePort();
        var gamePort = GetAvailablePort();
        while (gamePort == channelPort) gamePort = GetAvailablePort();
        var builder = Host.CreateApplicationBuilder();
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
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
        var stream = client.GetStream();
        _ = await ReadResponseAsync(stream, timeout.Token); // initial handshake notice

        await stream.WriteAsync(EncodeClientFrame(0x01B6, [9, 0, 0xFF, 0xFF, 0], 7),
            timeout.Token);
        Assert.Equal(GameServerFrameEncoder.Encode(0x01, 0x01B6, [0, 0x13]),
            await ReadResponseAsync(stream, timeout.Token));

        await stream.WriteAsync(EncodeClientFrame(0x0191, [9, 0, 0xFF, 0xFF], 8),
            timeout.Token);
        await logs.WaitForAsync("MAGIC_SEAL no selected session change=False", timeout.Token);
        await stream.WriteAsync(EncodeClientFrame(0x0003, [], 9), timeout.Token);
        Assert.Equal(GameServerFrameEncoder.Encode(0x01, 0x0003, [0x01]),
            await ReadResponseAsync(stream, timeout.Token));
        await host.StopAsync(timeout.Token);
    }

    private static byte[] EncodeClientFrame(ushort type, ReadOnlySpan<byte> body,
        ushort sequence)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength + body.Length];
        frame[0] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        // A21 reserves byte 11 and carries the sequence at bytes 12-13.
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

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        private readonly object gate = new();
        private readonly List<string> messages = [];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);

        public void Dispose() { }

        public async Task WaitForAsync(string expected, CancellationToken ct)
        {
            while (true)
            {
                lock (gate)
                    if (messages.Any(message => message.Contains(expected,
                            StringComparison.Ordinal))) return;
                await Task.Delay(10, ct);
            }
        }

        private sealed class CaptureLogger(CaptureLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.gate)
                    owner.messages.Add(formatter(state, exception));
            }
        }
    }
}
