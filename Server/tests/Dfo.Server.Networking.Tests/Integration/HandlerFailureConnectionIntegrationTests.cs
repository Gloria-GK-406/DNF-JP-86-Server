using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Networking.Tests.Characterization;
using Dfo.Server.Application;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>What the client sees when a Game handler throws: no reply to that frame, and the connection is closed.</summary>
/// <remarks>
/// The marriage use cases throw when a relation commit is refused after a child record changed (the command transaction
/// rolls back), and the marriage handlers do not catch it; this pins what that exception becomes at the socket.
/// </remarks>
public sealed class HandlerFailureConnectionIntegrationTests
{
    [Fact]
    public async Task Native_normal_party_edit_with_retained_target_keeps_the_connection_alive()
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
            await stream.WriteAsync(Frame(0x000C,
                Convert.FromHexString("01000C000000E697A0E9989FE4BC8DE5908D04000000000500020100")), timeout.Token);
            await stream.WriteAsync(Frame(3, []), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 3, [1]), await Read(stream, timeout.Token));
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task A_frame_whose_handler_throws_gets_no_reply_and_its_connection_is_closed_while_others_are_served()
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
        var original = builder.Services.Last(x => x.ServiceType == typeof(INetworkMessageDispatcher));
        builder.Services.Remove(original);
        builder.Services.AddSingleton<INetworkMessageDispatcher>(sp =>
            new ThrowingOnWeddingResponse((INetworkMessageDispatcher)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)));
        using var host = TestInfrastructure.Build(builder);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await host.StartAsync(timeout.Token);
        try
        {
            using var failing = new TcpClient();
            await failing.ConnectAsync(IPAddress.Loopback, game, timeout.Token);
            var stream = failing.GetStream(); _ = await Read(stream, timeout.Token);
            await stream.WriteAsync(Frame(0x03F1, NativeBody("0000000001000000")), timeout.Token);
            var buffer = new byte[64];
            Assert.Equal(0, await stream.ReadAsync(buffer, timeout.Token));

            using var healthy = new TcpClient();
            await healthy.ConnectAsync(IPAddress.Loopback, game, timeout.Token);
            var other = healthy.GetStream(); _ = await Read(other, timeout.Token);
            await other.WriteAsync(Frame(0x03F1, NativeBody("0000000001000001")), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 0x03F1, Convert.FromHexString("01FDFFFFFF0000000001000001")), await Read(other, timeout.Token));
            await other.WriteAsync(Frame(3, []), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 3, [1]), await Read(other, timeout.Token));
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    /// <summary>
    /// F-32 (DR-15, RQ-031(c)): the exception comes from inside the inbound job, from a domain service the real
    /// handler calls, so it travels dispatcher, executor, handler as in production. That connection gets no reply
    /// and is closed; the other is served. The executor's Error is the only Error, and it names the connection.
    /// The wire assertions come first: on the base only the log assertions after them may fail (TASK-31 RED).
    /// </summary>
    [Fact]
    public async Task A_handler_that_throws_inside_its_job_closes_only_its_connection_and_is_logged_once()
    {
        var channel = Port(); var game = Port(); while (game == channel) game = Port();
        var builder = Host.CreateApplicationBuilder(["--hostBuilder:reloadConfigOnChange=false"]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Networking:Enabled"] = "true", ["Networking:ChannelPort"] = channel.ToString(),
            ["Networking:GamePorts:0"] = game.ToString(), ["Game:EndpointHost"] = "127.0.0.1",
        });
        var logs = new CapturedLogs();
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IServerConfigService>(new ServerConfigService(builder.Configuration));
        builder.Services.AddSingleton<IChannelCatalog>(StaticChannelCatalog.ForGamePort(game));
        builder.Services.AddSingleton<IChannelScriptSource>(new NoChannelScript());
        builder.Services.AddSingleton(ChannelEntryOptions.Legacy);
        new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
        new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);
        new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);
        var armed = 1;
        NetworkConnectionId? failedSource = null;
        builder.Services.Decorate<ISelectedSessionDomainService>((_, inner) =>
            PostCommitFailureWireTests.Recording<ISelectedSessionDomainService>.Wrap(inner, (method, _) =>
            {
                if (method.Name != nameof(ISelectedSessionDomainService.FindByConnection)
                    || GameplayJob.Current is not { CommandType: "inbound:GameWeddingResponse" } chain
                    || Interlocked.Exchange(ref armed, 0) == 0) return;
                failedSource = chain.SourceConnection;
                throw new InvalidOperationException("Injected inside the wedding response job.");
            }));
        using var host = TestInfrastructure.Build(builder);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await host.StartAsync(timeout.Token);
        try
        {
            using var failing = new TcpClient();
            await failing.ConnectAsync(IPAddress.Loopback, game, timeout.Token);
            var stream = failing.GetStream(); _ = await Read(stream, timeout.Token);
            await stream.WriteAsync(Frame(0x03F1, NativeBody("0000000001000000")), timeout.Token);
            var buffer = new byte[64];
            Assert.Equal(0, await stream.ReadAsync(buffer, timeout.Token));

            using var healthy = new TcpClient();
            await healthy.ConnectAsync(IPAddress.Loopback, game, timeout.Token);
            var other = healthy.GetStream(); _ = await Read(other, timeout.Token);
            await other.WriteAsync(Frame(0x03F1, NativeBody("0000000001000001")), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 0x03F1, Convert.FromHexString("01FDFFFFFF0000000001000001")), await Read(other, timeout.Token));
            await other.WriteAsync(Frame(3, []), timeout.Token);
            Assert.Equal(GameServerFrameEncoder.Encode(1, 3, [1]), await Read(other, timeout.Token));
        }
        finally { await host.StopAsync(CancellationToken.None); }

        Assert.NotNull(failedSource);
        var failure = Assert.Single(logs.Entries, x => x.Message.StartsWith("Gameplay inbound:", StringComparison.Ordinal)
            && x.Message.Contains(" failed: root", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, failure.Level);
        Assert.StartsWith("Gameplay inbound:GameWeddingResponse failed", failure.Message, StringComparison.Ordinal);
        Assert.Equal("Injected inside the wedding response job.", Assert.IsType<InvalidOperationException>(failure.Exception).Message);
        Assert.DoesNotContain(logs.Entries, x => x.Level >= LogLevel.Error && x.Message.StartsWith("Game handler failed", StringComparison.Ordinal));
        Assert.Equal(failedSource, failure.Fields["SourceConnection"]);
        Assert.Single(logs.Entries, x => x.Level >= LogLevel.Error);
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception,
            IReadOnlyDictionary<string, object?> Fields)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        private sealed class Logger(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                    foreach (var pair in pairs) fields[pair.Key] = pair.Value;
                owner.Entries.Enqueue((logLevel, formatter(state, exception), exception, fields));
            }
        }
    }

    private sealed class ThrowingOnWeddingResponse(INetworkMessageDispatcher inner) : INetworkMessageDispatcher
    {
        public ValueTask DispatchAsync(NetworkDispatchContext context, IClientNetworkMessage message, CancellationToken cancellationToken) =>
            message.GetType().Name == "WeddingResponseRequest" && message.GetType().GetProperty("Outcome")!.GetValue(message) is (byte)0
                ? throw new InvalidOperationException("Marriage relation could not take AcceptProposal.")
                : inner.DispatchAsync(context, message, cancellationToken);
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

    private static int Port()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
