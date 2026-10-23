using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TouchSocket.Core;
using TouchSocket.Sockets;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Dfo.Server.Networking.Tests.Runtime;

/// <summary>
/// The connect and close callbacks are stream boundaries (plan, transitional rule 3) and hand the
/// connection to the lifecycle dispatcher; the transport drain waits for close callbacks in flight.
/// </summary>
public sealed class ConnectionCallbackBoundaryTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Connect_and_close_callbacks_called_inside_a_job_reach_the_lifecycle_dispatcher_off_the_executor()
    {
        var connections = new TouchSocketConnectionRegistry();
        var lifecycle = new RecordingLifecycle();
        var plugin = Plugin(connections, lifecycle);
        var session = Session("boundary");

        // A helper executor, not a container's: once the token is cleared the lifecycle CMD would
        // queue behind this very job on the same executor and the two would wait on each other.
        var (before, after) = await InJobAsync(async () =>
        {
            await plugin.OnTcpConnected(session, new ConnectedEventArgs());
            await plugin.OnTcpClosed(session, new ClosedEventArgs("test"));
        });

        Assert.True(before);
        Assert.Equal(["connected Game:boundary False", "disconnected Game:boundary False"], lifecycle.Seen);
        Assert.True(after);
        Assert.False(connections.TryGet(new("Game:boundary"), out _));
    }

    [Fact]
    public async Task The_transport_drain_waits_for_close_callbacks_still_in_flight()
    {
        var connections = new TouchSocketConnectionRegistry();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycle = new RecordingLifecycle { Hold = release.Task };
        var plugin = Plugin(connections, lifecycle);
        var first = Session("first");
        var second = Session("second");
        connections.Add(first, NetworkEndpointKind.Game);
        connections.Add(second, NetworkEndpointKind.Game);

        var closing = new[]
        {
            plugin.OnTcpClosed(first, new ClosedEventArgs("first")),
            plugin.OnTcpClosed(second, new ClosedEventArgs("second")),
        };
        var drain = Drain(connections, lifecycle).DrainAsync(CancellationToken.None).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.False(drain.IsCompleted);
        Assert.Equal(0, Proxy(first).CloseCount + Proxy(second).CloseCount);

        release.SetResult();
        await Task.WhenAll(closing).WaitAsync(Patience);
        await drain.WaitAsync(Patience);
        Assert.Equal(2, lifecycle.Seen.Count);
    }

    [Fact]
    public async Task The_transport_drain_closes_the_connections_still_registered_and_waits_for_their_close_callbacks()
    {
        var connections = new TouchSocketConnectionRegistry();
        var lifecycle = new RecordingLifecycle { Delay = TimeSpan.FromMilliseconds(200) };
        var plugin = Plugin(connections, lifecycle);
        var remaining = Session("remaining");
        // Like TouchSocket, closing the session runs its close callback.
        Proxy(remaining).OnClose = () => _ = plugin.OnTcpClosed(remaining, new ClosedEventArgs("drained"));
        connections.Add(remaining, NetworkEndpointKind.Game);

        await Drain(connections, lifecycle).DrainAsync(CancellationToken.None).AsTask().WaitAsync(Patience);

        // The close itself is detached; the teardown may have been the drain's own.
        await WaitUntilAsync(() => Proxy(remaining).CloseCount == 1);
        Assert.Equal(["disconnected Game:remaining False"], lifecycle.Seen);
        Assert.Equal(1, lifecycle.Completed);
    }

    [Fact]
    public async Task The_transport_drain_tears_down_a_connection_whose_close_callback_never_comes()
    {
        var connections = new TouchSocketConnectionRegistry();
        var lifecycle = new RecordingLifecycle();
        var plugin = Plugin(connections, lifecycle);
        var lost = Session("lost");
        connections.Add(lost, NetworkEndpointKind.Game);

        await Drain(connections, lifecycle).DrainAsync(CancellationToken.None).AsTask().WaitAsync(Patience);
        // A callback that does arrive late finds nothing left to tear down.
        await plugin.OnTcpClosed(lost, new ClosedEventArgs("late")).WaitAsync(Patience);

        await WaitUntilAsync(() => Proxy(lost).CloseCount == 1);
        Assert.Equal(["disconnected Game:lost False"], lifecycle.Seen);
        Assert.Equal(1, lifecycle.Completed);
    }

    [Fact]
    public async Task A_teardown_that_fails_in_the_drain_does_not_cost_the_other_connections_theirs()
    {
        var connections = new TouchSocketConnectionRegistry();
        var lifecycle = new RecordingLifecycle { FailFor = "Game:failing" };
        connections.Add(Session("failing"), NetworkEndpointKind.Game);
        connections.Add(Session("other"), NetworkEndpointKind.Game);
        var log = new ErrorLog();

        await new TouchSocketTransportDrain(connections, lifecycle, log)
            .DrainAsync(CancellationToken.None).AsTask().WaitAsync(Patience);

        Assert.Equal(["disconnected Game:failing False", "disconnected Game:other False"], lifecycle.Seen.Order());
        Assert.Equal(1, lifecycle.Completed);
        var error = Assert.Single(log.Errors);
        Assert.IsType<InvalidOperationException>(error);
    }

    private static GameNetworkPlugin Plugin(TouchSocketConnectionRegistry connections, INetworkConnectionLifecycleDispatcher lifecycle)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new GameNetworkPlugin(connections, lifecycle, new NoDispatcher(),
            new GameMessageDecoderRegistry([], services), NullLogger<GameNetworkPlugin>.Instance);
    }

    private static TouchSocketTransportDrain Drain(
        TouchSocketConnectionRegistry connections, INetworkConnectionLifecycleDispatcher lifecycle) =>
        new(connections, lifecycle, NullLogger<TouchSocketTransportDrain>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Patience);
        while (!condition()) await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
    }

    private static async Task<(bool Before, bool After)> InJobAsync(Func<Task> callback)
    {
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        return await executor.RunAsync(new GameplayWork<(bool, bool)>(
            GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection),
            "test:connection-boundary",
            async _ =>
            {
                var before = GameplayJob.IsOnExecutor;
                await callback();
                return (before, GameplayJob.IsOnExecutor);
            }), CancellationToken.None).AsTask().WaitAsync(Patience);
    }

    private sealed class RecordingLifecycle : INetworkConnectionLifecycleDispatcher
    {
        private int completed;

        public ConcurrentQueue<string> Seen { get; } = new();

        public Task Hold { get; init; } = Task.CompletedTask;

        public TimeSpan Delay { get; init; }

        /// <summary>The connection whose teardown throws, if any.</summary>
        public string? FailFor { get; init; }

        public int Completed => Volatile.Read(ref completed);

        public ValueTask DispatchConnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken)
        {
            Seen.Enqueue($"connected {connection.ConnectionId} {GameplayJob.IsOnExecutor}");
            return ValueTask.CompletedTask;
        }

        public async ValueTask DispatchDisconnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken)
        {
            Seen.Enqueue($"disconnected {connection.ConnectionId} {GameplayJob.IsOnExecutor}");
            if (connection.ConnectionId.Value == FailFor) throw new InvalidOperationException("teardown failed");
            await Hold;
            await Task.Delay(Delay, cancellationToken);
            Interlocked.Increment(ref completed);
        }
    }

    private sealed class ErrorLog : ILogger<TouchSocketTransportDrain>
    {
        public ConcurrentQueue<Exception?> Errors { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error) Errors.Enqueue(exception);
        }
    }

    private sealed class NoDispatcher : INetworkMessageDispatcher
    {
        public ValueTask DispatchAsync(NetworkDispatchContext context, IClientNetworkMessage message, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public interface ITestSession : ITcpSession, ITcpSessionClient;

    private static ITestSession Session(string id)
    {
        var session = DispatchProxy.Create<ITestSession, SessionProxy>();
        ((SessionProxy)(object)session).Id = id;
        return session;
    }

    private static SessionProxy Proxy(ITestSession session) => (SessionProxy)(object)session;

    public class SessionProxy : DispatchProxy
    {
        private int closeCount;

        public string Id { get; set; } = "";

        public Action OnClose { get; set; } = () => { };

        public int CloseCount => Volatile.Read(ref closeCount);

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "get_Id") return Id;
            if (method.Name is "get_LocalEndPoint" or "get_RemoteEndPoint") return new IPEndPoint(IPAddress.Loopback, 10011);
            if (method.Name == "CloseAsync")
            {
                Interlocked.Increment(ref closeCount);
                OnClose();
                return Task.FromResult(Result.Success);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
