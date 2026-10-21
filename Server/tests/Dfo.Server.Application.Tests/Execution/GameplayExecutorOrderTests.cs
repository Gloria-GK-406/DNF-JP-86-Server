using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>AC-001: one FIFO consumer, one CMD at a time, failures isolated, one shard.</summary>
public sealed class GameplayExecutorOrderTests
{
    [Fact]
    public async Task Concurrent_producers_see_their_work_run_in_enqueue_order_one_at_a_time()
    {
        const int producers = 8;
        const int perProducer = 250;
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var admission = new object();
        var nextTicket = 0;
        var executed = new List<int>();
        var active = 0;
        var overlaps = 0;
        var start = new ManualResetEventSlim();
        var pending = new List<Task>();

        var producerTasks = Enumerable.Range(0, producers).Select(_ => Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < perProducer; i++)
            {
                // RunAsync enqueues before it returns, so taking the ticket and enqueueing under
                // one lock makes the ticket order the enqueue order.
                lock (admission)
                {
                    var ticket = nextTicket++;
                    var throws = ticket % 97 == 0;
                    pending.Add(executor.RunAsync(ExecutionTest.Work(async ct =>
                    {
                        if (Interlocked.Increment(ref active) != 1) Interlocked.Increment(ref overlaps);
                        executed.Add(ticket);
                        if (ticket % 10 == 0) await Task.Yield();
                        Interlocked.Decrement(ref active);
                        if (throws) throw new InvalidOperationException($"ticket {ticket}");
                    }), CancellationToken.None).AsTask());
                }
            }
        })).ToArray();
        start.Set();
        await Task.WhenAll(producerTasks).WaitAsync(ExecutionTest.Patience);

        var outcomes = await Task.WhenAll(pending.Select(async task =>
        {
            try { await task; return true; }
            catch (InvalidOperationException) { return false; }
        })).WaitAsync(ExecutionTest.Patience);

        Assert.Equal(producers * perProducer, executed.Count);
        Assert.Equal(Enumerable.Range(0, producers * perProducer), executed);
        Assert.Equal(0, overlaps);
        Assert.Equal(
            Enumerable.Range(0, producers * perProducer).Count(ticket => ticket % 97 == 0),
            outcomes.Count(succeeded => !succeeded));
    }

    [Fact]
    public async Task A_failing_cmd_reaches_its_caller_and_the_next_cmd_still_runs()
    {
        var (executor, log) = ExecutionTest.Executor();
        await using var _executor = executor;
        var chain = ExecutionTest.Root();

        var failed = executor.RunAsync(ExecutionTest.Work<int>(
            _ => throw new InvalidOperationException("boom"), "Failing", chain), CancellationToken.None);
        var next = executor.RunAsync(ExecutionTest.Work(_ => ValueTask.FromResult(7)), CancellationToken.None);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await failed);
        Assert.Equal("boom", failure.Message);
        Assert.Equal(7, await next);
        var error = Assert.Single(log.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        Assert.Same(failure, error.Exception);
        Assert.Equal(chain.RootId, error.Fields["RootId"]);
        Assert.Equal("Failing", error.Fields["CommandType"]);
    }

    [Fact]
    public async Task Every_shard_key_selects_the_one_executor()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddGameplayExecution()
            .BuildServiceProvider();
        var selector = provider.GetRequiredService<GameplayShardSelector>();
        var executor = provider.GetRequiredService<GameplayExecutor>();

        var keys = new[]
        {
            GameplayShardKey.World,
            default,
            new GameplayShardKey("channel-1"),
            new GameplayShardKey("channel-99"),
            new GameplayShardKey(string.Empty),
        };

        Assert.All(keys, key => Assert.Same(executor, selector.Select(key)));
    }

    [Fact]
    public async Task The_queue_runs_work_on_the_executor_its_key_selects()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddGameplayExecution()
            .BuildServiceProvider();
        var queue = provider.GetRequiredService<IGameplayQueue>();

        var onExecutor = await queue.RunAsync(
            new GameplayWork<bool>(new GameplayShardKey("channel-7"), ExecutionTest.Root(), "Probe",
                _ => ValueTask.FromResult(GameplayJob.IsOnExecutor)),
            CancellationToken.None);
        var ran = false;
        await queue.RunAsync(
            new GameplayWork(GameplayShardKey.World, ExecutionTest.Root(), "Probe",
                _ => { ran = GameplayJob.IsOnExecutor; return ValueTask.CompletedTask; }),
            CancellationToken.None);

        Assert.True(onExecutor);
        Assert.True(ran);
    }
}
