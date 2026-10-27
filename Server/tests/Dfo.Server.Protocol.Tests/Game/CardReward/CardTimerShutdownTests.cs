using System.Collections.Concurrent;
using System.Reflection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Hosting;
using Dfo.Server.Application;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Protocol.Game.CardReward;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Tests.Game.CardReward;

/// <summary>
/// RQ-009 for the card timers: the coordinator is one of the executor's producer drains, so a host
/// that stops with a timer still in its delay cancels it before the executor stops, and takes no lock.
/// </summary>
public sealed class CardTimerShutdownTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [Fact]
    public void The_coordinator_is_registered_as_one_of_the_producer_drains()
    {
        using var provider = Compose(new ServiceCollection(), Configuration(Directory())).BuildServiceProvider();

        var coordinator = provider.GetRequiredService<DungeonCardPresentationCoordinator>();
        var drain = Assert.Single(provider.GetServices<IGameplayProducerDrain>(), x => ReferenceEquals(x, coordinator));
        Assert.Same(coordinator, drain);
        Assert.Same(coordinator, provider.GetRequiredService<IDungeonCardPresentationControl>());
    }

    [Fact]
    public async Task A_host_stopping_with_a_pending_layout_timer_cancels_it_before_the_executor_stops()
    {
        var directory = Directory();
        var delay = new UntilCancelled();
        var logs = new CapturedLogs();
        var observed = new AfterCoordinatorDrain();
        var builder = Host.CreateApplicationBuilder(["--hostBuilder:reloadConfigOnChange=false"]);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        Compose(builder.Services, Configuration(directory));
        builder.Services.AddSingleton<IDungeonCardPresentationDelay>(delay);
        // Registered after the coordinator, so it runs once the coordinator's drain has returned.
        builder.Services.AddSingleton<IGameplayProducerDrain>(sp => observed.Bind(sp));
        using var host = builder.Build();
        await host.StartAsync().WaitAsync(Patience);
        try
        {
            var coordinator = host.Services.GetRequiredService<DungeonCardPresentationCoordinator>();
            var session = SelectedSessionIdentity.Create(new AccountId(7), new CharacterId(7),
                new NetworkConnectionId("Game:card-timer"), 1, 10011);
            await host.Services.GetRequiredService<IGameplayQueue>().RunAsync(new GameplayWork(GameplayShardKey.World,
                GameplayCallChain.NewRoot(GameplayCallSource.Connection), "test:settlement", _ =>
                {
                    coordinator.ScheduleAfterSettlement(session, new DungeonRunId(Guid.NewGuid()));
                    return ValueTask.CompletedTask;
                }), CancellationToken.None);
            await delay.Waiting.Task.WaitAsync(Patience);
        }
        finally
        {
            await host.StopAsync().WaitAsync(Patience);
        }

        var executorStoppedFirst = await delay.Cancelled.Task.WaitAsync(Patience);
        Assert.False(executorStoppedFirst, "The layout timer was cancelled only after the executor stopped.");
        Assert.True(observed.Ran, "The drain after the coordinator's never ran.");
        Assert.False(observed.ExecutorStopped);
        Assert.Equal(0, observed.InFlight);
        Assert.DoesNotContain(logs.Messages, x => x.Contains("layout timer failed", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, x => x.Contains(nameof(GameplayExecutorStoppedException), StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, x => x.Contains("was not enqueued", StringComparison.Ordinal));
        Assert.Contains(logs.Messages, x => x.Contains("The gameplay executor stopped after draining its producers", StringComparison.Ordinal));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { System.IO.Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }

    private static string Directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dfo-card-timer-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "channel_info.etc"), [1]);
        return directory;
    }

    private static IConfiguration Configuration(string directory) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Path"] = Path.Combine(directory, "card.db"),
            ["Channel:ScriptPath"] = Path.Combine(directory, "channel_info.etc"),
        }).Build();

    private static IServiceCollection Compose(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<IGameServerReadiness, NoGame>();
        new InfrastructureModule().ConfigureServices(services, configuration);
        new PersistenceModule().ConfigureServices(services, configuration);
        new ApplicationModule().ConfigureServices(services, configuration);
        new Dfo.Server.Networking.NetworkingModule().ConfigureServices(services, configuration);
        new ProtocolModule().ConfigureServices(services, configuration);
        return services;
    }

    private sealed class NoGame : IGameServerReadiness
    {
        public bool NetworkEnabled => false;
        public bool IsReady => false;
    }

    /// <summary>A delay that only ends by cancellation, recording whether the executor had stopped by then.</summary>
    private sealed class UntilCancelled : IDungeonCardPresentationDelay
    {
        public GameplayExecutor? Executor { get; set; }
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Completes with whether the executor had already stopped when the delay was cancelled.</summary>
        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Waiting.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            finally
            {
                Cancelled.TrySetResult(Executor?.Completion.IsCompleted ?? true);
            }
        }
    }

    /// <summary>A drain registered after the coordinator: what it sees is what the coordinator left.</summary>
    private sealed class AfterCoordinatorDrain : IGameplayProducerDrain
    {
        private IServiceProvider? services;

        public bool Ran { get; private set; }
        public bool ExecutorStopped { get; private set; }
        public int InFlight { get; private set; } = -1;

        public AfterCoordinatorDrain Bind(IServiceProvider provider)
        {
            services = provider;
            var executor = provider.GetRequiredService<GameplayExecutor>();
            ((UntilCancelled)provider.GetRequiredService<IDungeonCardPresentationDelay>()).Executor = executor;
            return this;
        }

        public ValueTask DrainAsync(CancellationToken cancellationToken)
        {
            Ran = true;
            ExecutorStopped = services!.GetRequiredService<GameplayExecutor>().Completion.IsCompleted;
            var coordinator = services!.GetRequiredService<DungeonCardPresentationCoordinator>();
            InFlight = (int)typeof(DungeonCardPresentationCoordinator)
                .GetField("inflight", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        private sealed class Logger(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                owner.Messages.Enqueue($"{logLevel} {formatter(state, exception)} {exception?.GetType().Name}");
        }
    }
}
