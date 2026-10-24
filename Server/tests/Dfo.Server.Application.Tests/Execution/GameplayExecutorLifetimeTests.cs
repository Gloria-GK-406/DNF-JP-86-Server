using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// RQ-009 on the host: once every service has stopped, the producers are drained and only then is
/// the executor stopped, so whatever they enqueued still runs.
/// </summary>
public sealed class GameplayExecutorLifetimeTests
{
    [Fact]
    public async Task Stopped_drains_every_producer_in_order_runs_what_they_enqueued_then_stops_the_executor()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var steps = new ConcurrentQueue<string>();
        Task? enqueued = null;
        var first = new Drain(() =>
        {
            steps.Enqueue("first drain");
            enqueued = executor.RunAsync(ExecutionTest.Work(async _ =>
            {
                await Task.Delay(50);
                steps.Enqueue("job the first drain enqueued");
            }), CancellationToken.None).AsTask();
            return ValueTask.CompletedTask;
        });
        var second = new Drain(() =>
        {
            steps.Enqueue("second drain");
            return ValueTask.CompletedTask;
        });
        var lifetime = new GameplayExecutorLifetime(executor, [first, second], new RecordingExecutionLogger<GameplayExecutorLifetime>());

        await lifetime.StoppedAsync(CancellationToken.None).WaitAsync(ExecutionTest.Patience);

        Assert.True(executor.Completion.IsCompleted);
        Assert.True(enqueued!.IsCompletedSuccessfully);
        Assert.Equal(["first drain", "second drain", "job the first drain enqueued"], steps);
        await Assert.ThrowsAsync<GameplayExecutorStoppedException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None));
    }

    [Fact]
    public async Task Every_other_lifecycle_step_neither_starts_nor_stops_the_executor()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var drained = 0;
        var lifetime = new GameplayExecutorLifetime(executor, [new Drain(() =>
        {
            drained++;
            return ValueTask.CompletedTask;
        })], new RecordingExecutionLogger<GameplayExecutorLifetime>());

        await lifetime.StartingAsync(CancellationToken.None);
        await lifetime.StartAsync(CancellationToken.None);
        await lifetime.StartedAsync(CancellationToken.None);
        await lifetime.StoppingAsync(CancellationToken.None);
        await lifetime.StopAsync(CancellationToken.None);

        Assert.False(executor.IsStarted);
        Assert.False(executor.Completion.IsCompleted);
        Assert.Equal(0, drained);
    }

    [Fact]
    public async Task A_drain_that_fails_is_logged_and_the_next_drain_and_the_stop_still_happen()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var log = new RecordingExecutionLogger<GameplayExecutorLifetime>();
        var failure = new InvalidOperationException("drain failed");
        var secondRan = false;
        var lifetime = new GameplayExecutorLifetime(executor,
        [
            new Drain(() => ValueTask.FromException(failure)),
            new Drain(() =>
            {
                secondRan = true;
                return ValueTask.CompletedTask;
            }),
        ], log);

        await lifetime.StoppedAsync(CancellationToken.None).WaitAsync(ExecutionTest.Patience);

        Assert.True(secondRan);
        Assert.True(executor.Completion.IsCompleted);
        var warning = Assert.Single(log.Entries, x => x.Level == LogLevel.Warning);
        Assert.Same(failure, warning.Exception);
    }

    [Fact]
    public async Task A_host_assembled_from_the_application_module_has_stopped_its_executor_when_StopAsync_returns()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dfo-executor-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var script = Path.Combine(directory, "channel_info.etc");
            File.WriteAllBytes(script, [1]);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Path"] = Path.Combine(directory, "test.db"),
                ["Channel:ScriptPath"] = script,
            }).Build();
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddConfiguration(configuration);
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            new InfrastructureModule().ConfigureServices(builder.Services, configuration);
            new PersistenceModule().ConfigureServices(builder.Services, configuration);
            new ApplicationModule().ConfigureServices(builder.Services, configuration);
            // Networking disabled: its seams are registered, nothing listens.
            new Dfo.Server.Networking.NetworkingModule().ConfigureServices(builder.Services, configuration);
            using var host = builder.Build();
            await host.StartAsync();
            var executor = host.Services.GetRequiredService<GameplayExecutor>();
            var queue = host.Services.GetRequiredService<IGameplayQueue>();
            await queue.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None);
            Assert.False(executor.Completion.IsCompleted);

            await host.StopAsync(CancellationToken.None);

            Assert.True(executor.Completion.IsCompleted);
            await Assert.ThrowsAsync<GameplayExecutorStoppedException>(async () =>
                await queue.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None));
        }
        finally
        {
            SqliteTestPools.ClearOwned(Path.Combine(directory, "test.db"));
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A SQLite handle may outlive the host by a moment; the directory is under the temp root.
            }
        }
    }

    private sealed class Drain(Func<ValueTask> drain) : IGameplayProducerDrain
    {
        public ValueTask DrainAsync(CancellationToken cancellationToken) => drain();
    }
}
