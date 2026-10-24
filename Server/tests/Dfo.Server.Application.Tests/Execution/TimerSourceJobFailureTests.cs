using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Scheduling;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.Altar;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// A timer CMD can fail even when its body swallowed every error: a program error sticks to the job
/// (RQ-003). Such a failure must not end the hosted service, or the host stops with it (the default
/// <see cref="BackgroundServiceExceptionBehavior.StopHost"/>); the executor has already logged it.
/// </summary>
[Collection("Scheduler")]
public sealed class TimerSourceJobFailureTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    /// <summary>Every timer row of the reconciliation, and the login sweep (spec appendix A row 4).</summary>
    public static TheoryData<string, string> Sources()
    {
        var rows = new TheoryData<string, string>();
        foreach (var row in TimerSourceReconciliationTests.Sources)
            if ((GameplayCallSource)row[2] == GameplayCallSource.Timer)
                rows.Add((string)row[0], (string)row[1]);
        rows.Add("LoginSessionMaintenanceService", "timer:LoginSessionMaintenanceService");
        return rows;
    }

    /// <summary>
    /// Background services of the composition that are not timer sources, each with why its own
    /// loop is not proven above.
    /// </summary>
    private static readonly Dictionary<string, string> Exemptions = new(StringComparer.Ordinal)
    {
        ["DomainEventHostedService"] =
            "Event consumption, not a timer: a group whose CMD ends a job logs a Warning and backs off (DomainEventDispatcher.RunAsync, EndedAJob).",
    };

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task A_timer_source_whose_cmd_fails_with_a_program_error_keeps_running_and_runs_again(
        string service, string commandType)
    {
        StickyFault? fault = null;
        await using var host = new TimerSourceHost(wrapQueue: inner => fault = new StickyFault(inner, commandType));
        var hosted = Assert.IsAssignableFrom<BackgroundService>(host.Hosted(service));
        await host.StartAsync(hosted);

        await DriveAsync(host.Services, host.Clock, () => fault!.RunsAfterTheFault > 0 || hosted.ExecuteTask is { IsCompleted: true });

        Assert.True(fault!.Failed, $"{commandType} never ran its failing CMD.");
        var loop = Assert.IsAssignableFrom<Task>(hosted.ExecuteTask);
        Assert.False(loop.IsCompleted, $"{service} ended after its CMD failed: {loop.Exception?.GetBaseException()}");
        Assert.True(fault.RunsAfterTheFault > 0, $"{service} did not run {commandType} again.");
    }

    [Fact]
    public async Task Every_background_service_of_the_composition_is_a_proven_timer_source_or_a_listed_exemption()
    {
        await using var host = new TimerSourceHost();
        var actual = host.Services.GetServices<IHostedService>()
            .OfType<BackgroundService>()
            .Select(x => x.GetType().Name)
            .ToHashSet(StringComparer.Ordinal);
        var proven = Sources().Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(proven.Intersect(Exemptions.Keys));
        Assert.All(Exemptions.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
        Assert.Equal(proven.Union(Exemptions.Keys).Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_timer_cmd_that_fails_with_a_program_error_does_not_stop_the_host()
    {
        const string commandType = "timer:FatigueDailyRefreshService";
        var directory = Path.Combine(Path.GetTempPath(), "dfo-timer-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var script = Path.Combine(directory, "channel_info.etc");
            File.WriteAllBytes(script, [1]);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Path"] = Path.Combine(directory, "timer.db"),
                ["Channel:ScriptPath"] = script,
            }).Build();
            var clock = new SchedulerClock();
            StickyFault? fault = null;
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddConfiguration(configuration);
            new InfrastructureModule().ConfigureServices(builder.Services, configuration);
            new PersistenceModule().ConfigureServices(builder.Services, configuration);
            new ApplicationModule().ConfigureServices(builder.Services, configuration);
            // Networking disabled: its seams are registered, nothing listens.
            new Dfo.Server.Networking.NetworkingModule().ConfigureServices(builder.Services, configuration);
            new ProtocolModule().ConfigureServices(builder.Services, configuration);
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.AddSingleton<IGameplayQueue>(sp => fault = new StickyFault(
                new GameplayQueue(sp.GetRequiredService<GameplayShardSelector>()), commandType));
            using var host = builder.Build();
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var fatigue = host.Services.GetServices<IHostedService>().OfType<BackgroundService>()
                .Single(x => x.GetType().Name == "FatigueDailyRefreshService");
            await host.StartAsync();
            try
            {
                await DriveAsync(host.Services, clock,
                    () => fault!.RunsAfterTheFault > 0 || lifetime.ApplicationStopping.IsCancellationRequested);

                Assert.True(fault!.Failed);
                Assert.False(lifetime.ApplicationStopping.IsCancellationRequested, "The host is stopping after a timer CMD failed.");
                Assert.False(fatigue.ExecuteTask!.IsCompleted, fatigue.ExecuteTask.Exception?.GetBaseException().ToString());
                Assert.True(fault.RunsAfterTheFault > 0);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).WaitAsync(Patience);
            }
        }
        finally
        {
            SqliteTestFiles.Delete(directory, Path.Combine(directory, "timer.db"));
        }
    }

    /// <summary>Fires every manual timer and wakes both wake-up loops until <paramref name="done"/>.</summary>
    private static async Task DriveAsync(IServiceProvider services, SchedulerClock clock, Func<bool> done)
    {
        using var timeout = new CancellationTokenSource(Patience);
        while (!done())
        {
            clock.Advance(TimeSpan.FromDays(1));
            services.GetService<AltarWakeSignal>()?.Wake();
            services.GetService<IDungeonRoomMechanismWakeSignal>()?.Wake();
            await Task.Delay(20, timeout.Token);
        }
    }

    /// <summary>
    /// The first CMD of <paramref name="commandType"/> raises a program error and swallows it, as a tick
    /// body that catches per item would; the CMD fails anyway. Later CMDs of the type are counted.
    /// </summary>
    private sealed class StickyFault(IGameplayQueue inner, string commandType) : IGameplayQueue
    {
        private int injected;
        private int failed;
        private int after;

        public bool Failed => Volatile.Read(ref failed) == 1;

        public int RunsAfterTheFault => Volatile.Read(ref after);

        public ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken cancellationToken)
        {
            if (work.CommandType != commandType) return inner.RunAsync(work, cancellationToken);
            var body = work.Body;
            if (Interlocked.CompareExchange(ref injected, 1, 0) == 0)
                return inner.RunAsync(work with
                {
                    Body = async ct =>
                    {
                        try { GameplayJob.AssertNotOnExecutor("test:sticky-program-error"); }
                        catch (GameplaySelfWaitException) { }
                        try { return await body(ct).ConfigureAwait(false); }
                        finally { Volatile.Write(ref failed, 1); }
                    },
                }, cancellationToken);
            return inner.RunAsync(work with
            {
                Body = async ct =>
                {
                    var value = await body(ct).ConfigureAwait(false);
                    if (Failed) Interlocked.Increment(ref after);
                    return value;
                },
            }, cancellationToken);
        }

        public ValueTask RunAsync(GameplayWork work, CancellationToken cancellationToken)
        {
            var body = work.Body;
            return new ValueTask(RunAsync(new GameplayWork<bool>(work.Key, work.Chain, work.CommandType, async ct =>
            {
                await body(ct).ConfigureAwait(false);
                return true;
            }, work.Source), cancellationToken).AsTask());
        }
    }
}
