using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Dispatching;

/// <summary>
/// Connection establishment and loss run as one lifecycle job each on the container's executor, the
/// observers in registration order inside it (RQ-012, spec appendix A row 3).
/// </summary>
public sealed class ConnectionLifecycleDispatchTests
{
    [Fact]
    public async Task A_disconnect_runs_every_observer_in_order_as_one_lifecycle_job_on_the_container_executor()
    {
        var seen = new ConcurrentQueue<Seen>();
        await using var provider = Build(services =>
        {
            services.AddSingleton<Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler>(new Observer("first", seen));
            services.AddSingleton<Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler>(new Observer("second", seen));
        });
        var connection = Connection("ld1");
        var lifecycle = provider.GetRequiredService<INetworkConnectionLifecycleDispatcher>();

        await lifecycle.DispatchDisconnectedAsync(connection, CancellationToken.None);

        Assert.Equal(["first disconnected", "second disconnected"], seen.Select(x => x.Step));
        Assert.All(seen, x => AssertLifecycleJob(x, connection, "lifecycle:disconnected"));
        Assert.Single(seen.Select(x => x.Chain!.Value.RootId).Distinct());
        Assert.True(provider.GetRequiredService<GameplayExecutor>().IsStarted);
    }

    [Fact]
    public async Task A_connect_runs_every_observer_in_order_as_one_lifecycle_job_on_the_container_executor()
    {
        var seen = new ConcurrentQueue<Seen>();
        await using var provider = Build(services =>
        {
            services.AddSingleton<Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler>(new Observer("first", seen));
            services.AddSingleton<Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler>(new Observer("second", seen));
        });
        var connection = Connection("ld1c");

        await provider.GetRequiredService<INetworkConnectionLifecycleDispatcher>()
            .DispatchConnectedAsync(connection, CancellationToken.None);

        Assert.Equal(["first connected", "second connected"], seen.Select(x => x.Step));
        Assert.All(seen, x => AssertLifecycleJob(x, connection, "lifecycle:connected"));
        Assert.Single(seen.Select(x => x.Chain!.Value.RootId).Distinct());
    }

    [Fact]
    public async Task An_observer_that_throws_fails_the_dispatch_and_the_next_observer_does_not_run()
    {
        var seen = new ConcurrentQueue<Seen>();
        var failure = new InvalidOperationException("observer failed");
        await using var provider = Build(services =>
        {
            services.AddSingleton<Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler>(new Observer("first", seen, failure));
            services.AddSingleton<Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler>(new Observer("second", seen));
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => provider
            .GetRequiredService<INetworkConnectionLifecycleDispatcher>()
            .DispatchDisconnectedAsync(Connection("ld2"), CancellationToken.None).AsTask());

        Assert.Same(failure, error);
        Assert.Equal(["first disconnected"], seen.Select(x => x.Step));
    }

    [Fact]
    public async Task Dispatching_a_lifecycle_event_from_a_job_of_the_same_executor_is_a_self_wait()
    {
        await using var provider = Build(services =>
            services.AddSingleton<Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler>(new Observer("only", new())));
        var lifecycle = provider.GetRequiredService<INetworkConnectionLifecycleDispatcher>();

        await Assert.ThrowsAsync<GameplaySelfWaitException>(() => TestGameplay.RunAsync(provider, async () =>
            await lifecycle.DispatchDisconnectedAsync(Connection("ld3"), CancellationToken.None)));
    }

    [Fact]
    public async Task A_lifecycle_event_after_the_executor_stopped_is_refused_and_logged_as_not_run()
    {
        var seen = new ConcurrentQueue<Seen>();
        var log = new LogCapture();
        await using var provider = Build(services =>
        {
            services.AddLogging(logging => logging.AddProvider(log));
            services.AddSingleton<Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler>(new Observer("only", seen));
        });
        await provider.GetRequiredService<GameplayExecutor>().StopAsync(CancellationToken.None);
        var connection = Connection("ld4");

        await Assert.ThrowsAsync<GameplayExecutorStoppedException>(() => provider
            .GetRequiredService<INetworkConnectionLifecycleDispatcher>()
            .DispatchDisconnectedAsync(connection, CancellationToken.None).AsTask());

        Assert.Empty(seen);
        var warning = Assert.Single(log.Entries, x => x.Level == LogLevel.Warning);
        Assert.Contains("lifecycle:disconnected", warning.Message, StringComparison.Ordinal);
        Assert.Contains(connection.ConnectionId.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.Contains("did not run", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_observers_nothing_is_enqueued()
    {
        await using var provider = Build(_ => { });
        var lifecycle = provider.GetRequiredService<INetworkConnectionLifecycleDispatcher>();

        await lifecycle.DispatchConnectedAsync(Connection("ld5"), CancellationToken.None);
        await lifecycle.DispatchDisconnectedAsync(Connection("ld5"), CancellationToken.None);

        Assert.False(provider.GetRequiredService<GameplayExecutor>().IsStarted);
    }

    private static void AssertLifecycleJob(Seen seen, NetworkConnectionContext connection, string commandType)
    {
        Assert.True(seen.OnExecutor);
        var chain = Assert.NotNull(seen.Chain);
        Assert.Equal(GameplayCallSource.Lifecycle, chain.Source);
        Assert.Equal(connection.ConnectionId, chain.SourceConnection);
        Assert.Equal(commandType, chain.CommandType);
        Assert.Equal(0, chain.Depth);
    }

    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddNetworkMessageDispatching();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static NetworkConnectionContext Connection(string id) =>
        new(new("lifecycle-" + id + "-" + Guid.NewGuid().ToString("N")), NetworkEndpointKind.Game, 10011);

    private sealed record Seen(string Step, bool OnExecutor, GameplayCallChain? Chain);

    private sealed class Observer(string name, ConcurrentQueue<Seen> seen, Exception? failure = null) : Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler
    {
        public ValueTask ConnectedAsync(Dfo.Server.Application.Commands.CommandExecutionContext execution, NetworkConnectionContext connection, CancellationToken cancellationToken) =>
            Record("connected");

        public ValueTask DisconnectedAsync(Dfo.Server.Application.Commands.CommandExecutionContext execution, NetworkConnectionContext connection, CancellationToken cancellationToken) =>
            Record("disconnected");

        private ValueTask Record(string what)
        {
            seen.Enqueue(new($"{name} {what}", GameplayJob.IsOnExecutor, GameplayJob.Current));
            return failure is null ? ValueTask.CompletedTask : ValueTask.FromException(failure);
        }
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(LogCapture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel)) owner.Entries.Enqueue((logLevel, formatter(state, exception)));
            }
        }
    }
}
