using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Application;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class InventorySlotMappingFailureIntegrationTests
{
    [Fact]
    public async Task An_unmappable_slot_is_logged_on_its_connection_and_the_host_serves_the_next_connection()
    {
        var logs = new RecordingLoggerProvider();
        var channelPort = GetAvailablePort();
        var gamePort = GetAvailablePort();
        while (gamePort == channelPort)
            gamePort = GetAvailablePort();

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

        using (var failingClient = new TcpClient())
        {
            await failingClient.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
            var failingStream = failingClient.GetStream();
            _ = await ReadResponseAsync(failingStream, timeout.Token);
            await failingStream.WriteAsync(
                EncodeClientFrame(0x01, 0x0016, SellBody(0xFF, 65, 1, 2)),
                timeout.Token);
        }

        using var healthyClient = new TcpClient();
        await healthyClient.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
        var healthyStream = healthyClient.GetStream();
        _ = await ReadResponseAsync(healthyStream, timeout.Token);
        await healthyStream.WriteAsync(EncodeClientFrame(0x01, 0x0003, []), timeout.Token);
        Assert.Equal(
            GameServerFrameEncoder.Encode(0x01, 0x0003, [0x01]),
            await ReadResponseAsync(healthyStream, timeout.Token));

        var failure = await WaitForErrorAsync(logs, timeout.Token);
        Assert.IsType<InventorySlotMappingException>(failure.Exception);
        Assert.Contains("255/65", failure.Exception.Message, StringComparison.Ordinal);

        await host.StopAsync(timeout.Token);
    }

    private static async Task<RecordingLoggerProvider.LogEntry> WaitForErrorAsync(
        RecordingLoggerProvider logs,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var entries = logs.ErrorsContaining("Gameplay inbound:");
            if (entries.Count > 0)
                return Assert.Single(entries);

            await Task.Delay(10, cancellationToken);
        }
    }

    private static byte[] SellBody(byte space, short slot, short count, int npc)
    {
        var body = new byte[13];
        body[0] = space;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), slot);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(3), count);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5), npc);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9), space + slot + count);
        return body;
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

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly Lock gate = new();
        private readonly List<LogEntry> entries = [];

        public IReadOnlyList<LogEntry> ErrorsContaining(string fragment)
        {
            lock (gate)
            {
                return entries
                    .Where(entry => entry.Level == LogLevel.Error
                        && entry.Message.Contains(fragment, StringComparison.Ordinal))
                    .ToArray();
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void Dispose()
        {
        }

        private void Add(LogLevel level, string message, Exception? exception)
        {
            lock (gate)
                entries.Add(new(level, message, exception));
        }

        public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

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
                owner.Add(logLevel, formatter(state, exception), exception);
        }
    }
}
