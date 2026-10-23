using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Channel.Connect;
using Dfo.Server.Protocol.Game.Exit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// A real TouchSocket host composed like the inbound wire tests, recording the connection lifecycle:
/// an observer after the real ones, a probe handler for GameExit and ChannelConnect, and a decorator
/// on the lifecycle dispatcher that sees the close callback before it enqueues.
/// </summary>
internal sealed class LifecycleWireHost : IAsyncDisposable
{
    public const ushort ExitType = 0x0003;
    public const ushort BlobType = 0x0FFE;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly CancellationTokenSource timeout = new(Patience);
    private readonly List<TcpClient> clients = [];
    private readonly int channelPort;
    private readonly int gamePort;
    private bool stopped;

    private LifecycleWireHost(TestInfrastructure host, LifecycleRecorder recorder, LifecycleLog log, int channelPort, int gamePort)
    {
        Host = host;
        Recorder = recorder;
        Log = log;
        this.channelPort = channelPort;
        this.gamePort = gamePort;
    }

    public TestInfrastructure Host { get; }

    public LifecycleRecorder Recorder { get; }

    public LifecycleLog Log { get; }

    public CancellationToken Timeout => timeout.Token;

    public GameplayExecutor Executor => Host.Services.GetRequiredService<GameplayExecutor>();

    public static async Task<LifecycleWireHost> StartAsync(Action<IServiceCollection>? configure = null)
    {
        var channel = Port(); var game = Port(); while (game == channel) game = Port();
        var recorder = new LifecycleRecorder();
        var log = new LifecycleLog();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Networking:Enabled"] = "true", ["Networking:ChannelPort"] = channel.ToString(),
            ["Networking:GamePorts:0"] = game.ToString(), ["Game:EndpointHost"] = "127.0.0.1",
            ["GameLogin:Environment"] = "1", ["GameLogin:ConnectionPort"] = "10128",
            ["GameLogin:EndpointPortA"] = "17200", ["GameLogin:EndpointPortB"] = "17201",
        });
        builder.Logging.AddProvider(log);
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IServerConfigService>(new ServerConfigService(builder.Configuration));
        builder.Services.AddSingleton<IChannelCatalog>(StaticChannelCatalog.ForGamePort(game));
        builder.Services.AddSingleton<IChannelScriptSource>(new NoChannelScript());
        builder.Services.AddSingleton(ChannelEntryOptions.Legacy);
        new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
        new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);
        new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(recorder);
        // After every real observer, so it sees the connection once they have handled it.
        builder.Services.AddSingleton<INetworkConnectionObserver>(sp =>
            new RecordingObserver(recorder, sp.GetRequiredService<GameplayExecutor>()));
        foreach (var type in new[] { ClientMessageType.GameExit, ClientMessageType.ChannelConnect })
            builder.Services.Remove(builder.Services.Single(x => x.ServiceType == typeof(MessageHandlerRegistration)
                && ((MessageHandlerRegistration)x.ImplementationInstance!).MessageType == type));
        builder.Services.RegisterMessageHandler<ProbeHandler>(ClientMessageType.GameExit);
        builder.Services.RegisterMessageHandler<ProbeHandler>(ClientMessageType.ChannelConnect);
        builder.Services.AddServerMessageEncoder<LifecycleBlob, LifecycleBlobEncoder>(NetworkEndpointKind.Game);
        var original = builder.Services.Last(x => x.ServiceType == typeof(INetworkConnectionLifecycleDispatcher));
        builder.Services.Remove(original);
        builder.Services.AddSingleton<INetworkConnectionLifecycleDispatcher>(sp => new MarkerRecordingLifecycle(
            (INetworkConnectionLifecycleDispatcher)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), recorder));
        configure?.Invoke(builder.Services);
        var host = TestInfrastructure.Build(builder);
        var started = new LifecycleWireHost(host, recorder, log, channel, game);
        try
        {
            await host.StartAsync(started.Timeout);
        }
        catch
        {
            await started.DisposeAsync();
            throw;
        }
        return started;
    }

    /// <summary>Connects, reads the initial notice and returns the server's ID for the connection.</summary>
    public async Task<(TcpClient Client, NetworkConnectionId Id)> ConnectGameAsync(int receiveBufferSize = 0)
    {
        var known = Recorder.Connected().ToHashSet();
        var client = receiveBufferSize > 0 ? new TcpClient { ReceiveBufferSize = receiveBufferSize } : new TcpClient();
        clients.Add(client);
        await client.ConnectAsync(IPAddress.Loopback, gamePort, Timeout);
        await ReadFrameAsync(client, Timeout);
        return (client, await NewConnectionAsync(known, "Game:"));
    }

    public async Task<(TcpClient Client, NetworkConnectionId Id)> ConnectChannelAsync()
    {
        var known = Recorder.Connected().ToHashSet();
        var client = new TcpClient();
        clients.Add(client);
        await client.ConnectAsync(IPAddress.Loopback, channelPort, Timeout);
        return (client, await NewConnectionAsync(known, "Channel:"));
    }

    public Task RunAsync(string commandType, Func<CancellationToken, ValueTask> body) =>
        Host.Services.GetRequiredService<IGameplayQueue>().RunAsync(new GameplayWork(
            GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.Connection), commandType, body), Timeout).AsTask();

    /// <summary>A job enqueued now; returns how long it took to run.</summary>
    public async Task<TimeSpan> NextJobLatencyAsync()
    {
        var watch = Stopwatch.StartNew();
        await RunAsync("test:next", _ => ValueTask.CompletedTask).WaitAsync(TimeSpan.FromSeconds(1));
        return watch.Elapsed;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        stopped = true;
        await Host.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients) client.Dispose();
        try
        {
            if (!stopped) await Host.StopAsync(CancellationToken.None);
        }
        finally
        {
            Host.Dispose();
            timeout.Dispose();
        }
    }

    public static byte[] GameFrame(ushort type, ushort sequence, ReadOnlySpan<byte> body)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength + body.Length]; frame[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12), sequence);
        body.CopyTo(frame.AsSpan(GameClientFrameRequestInfo.HeaderLength)); return frame;
    }

    public static async Task<byte[]> ReadFrameAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var header = new byte[GameServerFrameEncoder.HeaderLength];
        await client.GetStream().ReadExactlyAsync(header, cancellationToken);
        var frame = new byte[checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3)))];
        header.CopyTo(frame, 0);
        await client.GetStream().ReadExactlyAsync(frame.AsMemory(header.Length), cancellationToken);
        return frame;
    }

    /// <summary>True when the peer's next read is the end of the stream (FIN), false on a reset.</summary>
    public static async Task<bool> ReadsFinAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        try
        {
            return await client.GetStream().ReadAsync(buffer, cancellationToken) == 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
    }

    private async Task<NetworkConnectionId> NewConnectionAsync(HashSet<NetworkConnectionId> known, string prefix)
    {
        NetworkConnectionId? found = null;
        await WaitUntilAsync(() => (found = Recorder.Connected()
            .FirstOrDefault(x => !known.Contains(x) && x.Value.StartsWith(prefix, StringComparison.Ordinal))) is { Value: not null }, Timeout);
        return found!.Value;
    }

    private static int Port()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

internal sealed record LifecycleEvent(
    long Order,
    string Kind,
    NetworkConnectionId Connection,
    bool OnExecutor,
    GameplayCallChain? Chain,
    bool Flag = false,
    long Ticks = 0);

internal sealed class LifecycleRecorder
{
    private long order;

    /// <summary>Set by a job to see whether a close callback inherits its execution context.</summary>
    public static readonly AsyncLocal<string?> Marker = new();

    public ConcurrentQueue<LifecycleEvent> Events { get; } = new();

    public TimeSpan DisconnectDelay { get; set; }

    /// <summary>What the probe handler does between recording its entry and its exit.</summary>
    public Func<NetworkDispatchContext, INetworkMessageSender, INetworkConnectionCloser, CancellationToken, Task> OnInbound { get; set; } =
        (_, _, _, _) => Task.CompletedTask;

    public void Record(string kind, NetworkConnectionId connection, bool flag = false) => Events.Enqueue(new(
        Interlocked.Increment(ref order), kind, connection, GameplayJob.IsOnExecutor, GameplayJob.Current, flag, Stopwatch.GetTimestamp()));

    public IEnumerable<NetworkConnectionId> Connected() => Events.Where(x => x.Kind == "connected").Select(x => x.Connection);

    public LifecycleEvent? First(string kind, NetworkConnectionId connection) =>
        Events.FirstOrDefault(x => x.Kind == kind && x.Connection == connection);

    public string[] KindsOf(NetworkConnectionId connection, params string[] kinds) =>
        [.. Events.Where(x => x.Connection == connection && kinds.Contains(x.Kind)).OrderBy(x => x.Order).Select(x => x.Kind)];
}

internal sealed class RecordingObserver(LifecycleRecorder recorder, GameplayExecutor executor) : INetworkConnectionObserver
{
    public ValueTask ConnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken)
    {
        recorder.Record("connected", connection.ConnectionId);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisconnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken)
    {
        // Flag: whether the executor had already stopped when the teardown ran.
        recorder.Record("disconnect-enter", connection.ConnectionId, executor.Completion.IsCompleted);
        if (recorder.DisconnectDelay > TimeSpan.Zero) await Task.Delay(recorder.DisconnectDelay, CancellationToken.None);
        recorder.Record("disconnect-exit", connection.ConnectionId);
    }
}

internal sealed class ProbeHandler(LifecycleRecorder recorder, INetworkMessageSender sender, INetworkConnectionCloser closer)
    : IClientMessageHandler
{
    public bool CanHandle(IClientNetworkMessage message) => message is GameExitRequest or ChannelConnectRequest;

    public async ValueTask HandleAsync(NetworkDispatchContext context, IClientNetworkMessage message, CancellationToken cancellationToken)
    {
        recorder.Record("inbound-enter", context.Connection.ConnectionId);
        await recorder.OnInbound(context, sender, closer, cancellationToken);
        recorder.Record("inbound-exit", context.Connection.ConnectionId);
    }
}

/// <summary>Sees the close callback before the real dispatcher enqueues; the flag is whether a job's marker is visible.</summary>
internal sealed class MarkerRecordingLifecycle(INetworkConnectionLifecycleDispatcher inner, LifecycleRecorder recorder)
    : INetworkConnectionLifecycleDispatcher
{
    public ValueTask DispatchConnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken) =>
        inner.DispatchConnectedAsync(connection, cancellationToken);

    public async ValueTask DispatchDisconnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken)
    {
        recorder.Record("closed-callback", connection.ConnectionId, LifecycleRecorder.Marker.Value is not null);
        await inner.DispatchDisconnectedAsync(connection, cancellationToken);
        recorder.Record("closed-returned", connection.ConnectionId);
    }
}

internal sealed record LifecycleBlob(int Index, int Size) : IServerNetworkMessage
{
    public ServerMessageType MessageType => default;
}

internal sealed class LifecycleBlobEncoder : ServerMessageEncoder<LifecycleBlob>
{
    protected override IReadOnlyList<byte[]> EncodeMessage(LifecycleBlob message)
    {
        var body = new byte[Math.Max(4, message.Size)];
        BinaryPrimitives.WriteInt32LittleEndian(body, message.Index);
        return One(GameServerFrameEncoder.Encode(0, LifecycleWireHost.BlobType, body));
    }
}

internal sealed class LifecycleLog : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Category, string Message)> Entries { get; } = new();

    public int Count(string fragment) => Entries.Count(x => x.Message.Contains(fragment, StringComparison.Ordinal));

    public int Count(string fragment, LogLevel level) =>
        Entries.Count(x => x.Level == level && x.Message.Contains(fragment, StringComparison.Ordinal));

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(LifecycleLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Entries.Enqueue((logLevel, category, formatter(state, exception)));
        }
    }
}
