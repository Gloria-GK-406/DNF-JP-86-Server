using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Commands;

/// <summary>
/// Plan transition rule 1: off the executor <c>SendAsync</c> enqueues the command on its container's
/// executor and waits; on a job of any executor it runs inline. There is no "no executor, run inline" path.
/// </summary>
public sealed class CommandBusExecutorTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task F1_a_send_from_off_the_executor_runs_as_its_own_bus_job()
    {
        await using var world = World.Build();

        var result = await world.Bus.SendAsync(new Probe(41), CancellationToken.None).AsTask().WaitAsync(Patience);

        Assert.Equal(42, result.Value);
        var seen = Assert.Single(world.Seen);
        Assert.True(seen.OnExecutor);
        Assert.Equal("bus:" + nameof(Probe), seen.CommandType);
        Assert.Equal(GameplayCallSource.Timer, seen.Source);
        Assert.Equal(1, world.Queue.Enqueued);
    }

    [Fact]
    public async Task F2_a_send_from_a_job_is_refused_without_inline_execution()
    {
        await using var world=World.Build();
        await Assert.ThrowsAsync<GameplaySelfWaitException>(()=>TestGameplay.RunAsync(world.Services,async()=>await world.Bus.SendAsync(new Probe(1))));
        Assert.Empty(world.Seen);Assert.Equal(0,world.Queue.Enqueued);
    }

    [Fact]
    public async Task F3_the_handlers_exception_reaches_the_caller_unchanged()
    {
        var failure = new InvalidOperationException("probe failure");
        await using var world = World.Build(failure);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await world.Bus.SendAsync(new Probe(1), CancellationToken.None).AsTask().WaitAsync(Patience));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task F4_cancelling_while_queued_never_runs_the_handler()
    {
        await using var world = World.Build();
        var executor = world.Services.GetRequiredService<GameplayExecutor>();
        var blocking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = executor.RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection), "test:blocker", async _ =>
            {
                entered.TrySetResult();
                await blocking.Task;
            }), CancellationToken.None).AsTask();
        await entered.Task.WaitAsync(Patience);

        using var cancellation = new CancellationTokenSource();
        var queued = world.Bus.SendAsync(new Probe(1), cancellation.Token).AsTask();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(Patience));
        blocking.TrySetResult();
        await blocker.WaitAsync(Patience);
        await executor.RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection), "test:after",
            _ => ValueTask.CompletedTask), CancellationToken.None).AsTask().WaitAsync(Patience);
        Assert.Empty(world.Seen);
    }

    [Fact]
    public async Task F5_a_handler_dispatching_again_is_still_a_nested_dispatch()
    {
        await using var world = World.Build(nested: true);

        var exception = await Assert.ThrowsAsync<GameplaySelfWaitException>(async () =>
            await world.Bus.SendAsync(new Probe(1), CancellationToken.None).AsTask().WaitAsync(Patience));

        Assert.Contains("Nested command dispatch", exception.Message, StringComparison.Ordinal);
        Assert.Single(world.Seen);
    }

    [Fact]
    public async Task F6_after_the_executor_stopped_an_off_executor_send_is_refused_not_run_inline()
    {
        await using var world = World.Build();
        await world.Services.GetRequiredService<GameplayExecutor>().StopAsync(CancellationToken.None);

        await Assert.ThrowsAsync<GameplayExecutorStoppedException>(async () =>
            await world.Bus.SendAsync(new Probe(1), CancellationToken.None).AsTask().WaitAsync(Patience));

        Assert.Empty(world.Seen);
    }

    [Fact]
    public async Task F7_concurrent_off_executor_sends_never_overlap()
    {
        await using var world = World.Build(yieldInside: true);

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => Task.Run(() => world.Bus.SendAsync(new Probe(i), CancellationToken.None).AsTask())))
            .WaitAsync(Patience);

        Assert.Equal(8, world.Seen.Count);
        Assert.Equal(1, world.Gauge.Maximum);
    }

    [Fact]
    public async Task F8_a_detached_internal_command_without_an_explicit_origin_is_refused()
    {
        await using var world = World.Build(leak: Leak.Detached);

        await world.Bus.SendAsync(new Probe(1), CancellationToken.None).AsTask().WaitAsync(Patience);

        var leaked = world.Services.GetRequiredService<Settings>().Leaked!;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => leaked.WaitAsync(Patience));
        Assert.Contains("explicit origin", exception.Message, StringComparison.Ordinal);
        Assert.Single(world.Seen);
    }

    [Fact]
    public async Task F9_a_continuation_that_outlived_its_job_is_a_leaked_flow()
    {
        await using var world = World.Build(leak: Leak.Continuation);
        var settings = world.Services.GetRequiredService<Settings>();

        await world.Bus.SendAsync(new Probe(1), CancellationToken.None).AsTask().WaitAsync(Patience);
        settings.Release.TrySetResult();

        var exception = await Assert.ThrowsAsync<GameplayLeakedFlowException>(() => settings.Leaked!.WaitAsync(Patience));
        Assert.Contains("leaked from a finished", exception.Message, StringComparison.Ordinal);
        Assert.Single(world.Seen);
    }

    [Fact]
    public async Task L1_a_flow_leaked_from_a_finished_job_is_refused_and_never_queued()
    {
        await using var world = World.Build();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new System.Collections.Concurrent.ConcurrentQueue<GameplayAssertionViolation>();
        // Started inside the job without suppressing the flow, so it carries the job's token past the job's end.
        var leaked = await TestGameplay.RunAsync(world.Services, () => Task.FromResult(Task.Run(async () =>
        {
            await release.Task;
            Execution.ViolationCapture.Attach(seen);
            return await world.Bus.SendAsync(new Probe(5), CancellationToken.None);
        }))).WaitAsync(Patience);
        release.TrySetResult();

        var thrown = await Assert.ThrowsAsync<GameplayLeakedFlowException>(() => leaked.WaitAsync(Patience));

        Assert.Contains("bus:" + nameof(Probe), thrown.Message, StringComparison.Ordinal);
        Assert.Empty(world.Seen);
        Assert.Equal(0, world.Queue.Enqueued);
        var violation = Assert.Single(seen);
        Assert.Equal(GameplayLeakAssertion.LeakedFlowKind, violation.Kind);
    }

    [Fact]
    public async Task L2_the_same_leaked_flow_detached_first_is_an_ordinary_off_executor_send()
    {
        await using var world = World.Build();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leaked = await TestGameplay.RunAsync(world.Services, () => Task.FromResult(Task.Run(async () =>
        {
            await release.Task;
            return await GameplayJob.RunDetachedAsync(() => world.Bus.SendAsync(new Probe(5), CancellationToken.None));
        }))).WaitAsync(Patience);
        release.TrySetResult();

        var result = await leaked.WaitAsync(Patience);

        Assert.Equal(6, result.Value);
        var seen = Assert.Single(world.Seen);
        Assert.True(seen.OnExecutor);
        Assert.Equal("bus:" + nameof(Probe), seen.CommandType);
        Assert.Equal(1, world.Queue.Enqueued);
    }

    [Fact]
    public async Task L4_a_null_command_fails_the_returned_task_instead_of_throwing_at_the_call()
    {
        await using var world = World.Build();

        var pending = world.Bus.SendAsync<ProbeResult>(null!, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentNullException>(() => pending.AsTask());
        Assert.Equal(0, world.Queue.Enqueued);
    }

    private enum Leak { None, Detached, Continuation }

    private sealed record Probe(int Value) : ICommand<ProbeResult>;

    private sealed record ProbeResult(int Value);

    private sealed record Observation(bool OnExecutor, string? CommandType, Guid RootId, GameplayCallSource Source);

    private sealed class Settings(Exception? failure, bool nested, bool yieldInside, Leak leak)
    {
        public Exception? Failure { get; } = failure;
        public bool Nested { get; } = nested;
        public bool YieldInside { get; } = yieldInside;
        public Leak Leak { get; } = leak;
        public Task<ProbeResult>? Leaked { get; set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Gauge
    {
        private int current;
        private int maximum;

        public int Maximum => Volatile.Read(ref maximum);

        public void Enter()
        {
            var now = Interlocked.Increment(ref current);
            int seen;
            while (now > (seen = Volatile.Read(ref maximum)) && Interlocked.CompareExchange(ref maximum, now, seen) != seen)
            {
            }
        }

        public void Exit() => Interlocked.Decrement(ref current);
    }

    private sealed class ProbeHandler(
        Settings settings,
        System.Collections.Concurrent.ConcurrentQueue<Observation> seen,
        Gauge gauge,
        ICommandBus bus) : ICommandHandler<Probe, ProbeResult>
    {
        public async ValueTask<ProbeResult> HandleAsync(
            CommandExecutionContext context, Probe command, CancellationToken cancellationToken)
        {
            var chain = GameplayJob.Current;
            seen.Enqueue(new Observation(GameplayJob.IsOnExecutor, chain?.CommandType,
                chain?.RootId ?? Guid.Empty, chain?.Source ?? default));
            gauge.Enter();
            try
            {
                if (settings.YieldInside) await Task.Delay(20, cancellationToken);
                if (settings.Failure is { } failure) throw failure;
                if (settings.Nested) return await bus.SendAsync(new Probe(command.Value), cancellationToken);
                // Work that leaves the handler's flow: not awaited here, the test inspects it afterwards.
                if (settings.Leak == Leak.Detached && command.Value == 1)
                    settings.Leaked = GameplayJob.RunDetached(() => bus.SendAsync(new Probe(2), CancellationToken.None).AsTask());
                if (settings.Leak == Leak.Continuation && command.Value == 1)
                    settings.Leaked = Task.Run(async () =>
                    {
                        await settings.Release.Task;
                        return await bus.SendAsync(new Probe(2), CancellationToken.None);
                    });
                return new ProbeResult(command.Value + 1);
            }
            finally
            {
                gauge.Exit();
            }
        }
    }

    private sealed class CountingQueue(IGameplayQueue inner) : IGameplayQueue
    {
        private int enqueued;

        public int Enqueued => Volatile.Read(ref enqueued);

        public ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref enqueued);
            return inner.RunAsync(work, cancellationToken);
        }

        public ValueTask RunAsync(GameplayWork work, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref enqueued);
            return inner.RunAsync(work, cancellationToken);
        }
    }

    private sealed class World(ServiceProvider services) : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;
        public ICommandBus Bus => DomainCommandFixture.Bus(Services);
        public CountingQueue Queue => (CountingQueue)Services.GetRequiredService<IGameplayQueue>();
        public IReadOnlyCollection<Observation> Seen =>
            Services.GetRequiredService<System.Collections.Concurrent.ConcurrentQueue<Observation>>();
        public Gauge Gauge => Services.GetRequiredService<Gauge>();

        public static World Build(
            Exception? failure = null, bool nested = false, bool yieldInside = false, Leak leak = Leak.None)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(new Settings(failure, nested, yieldInside, leak));
            services.AddSingleton<System.Collections.Concurrent.ConcurrentQueue<Observation>>();
            services.AddSingleton<Gauge>();
            // Registered first, so AddGameplayExecution's TryAdd keeps this counting queue.
            services.AddSingleton<IGameplayQueue>(provider =>
                new CountingQueue(new GameplayQueue(provider.GetRequiredService<GameplayShardSelector>())));
            services.AddCommandDispatching();
            services.AddFixtureCommand<Probe, ProbeResult, ProbeHandler>(FixtureUnitKind.Direct);
            return new World(services.AddSessionSafetyStandIns().BuildServiceProvider());
        }

        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }
}
