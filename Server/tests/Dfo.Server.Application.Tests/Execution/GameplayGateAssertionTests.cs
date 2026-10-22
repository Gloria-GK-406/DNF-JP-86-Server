using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Pipeline;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Events;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Scheduling;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Protocol.Game.UseStackable;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// Plan transition rule 2 for the Application gates: taking one off the executor, or on the executor
/// when it would have to wait, throws at once; on the executor the violation sticks to the job, and
/// every violation is reported to the diagnostic source. Removed with the gates in T-005.
/// </summary>
/// <remarks>A counterexample class the zero-violation evidence subtracts: 21 violations per run.</remarks>
public sealed class GameplayGateAssertionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    public static TheoryData<string> Gates => new()
    {
        nameof(OnlineUserExecutionGate),
        nameof(AccountExecutionGate),
        nameof(SelectedSessionTransitionGate),
        "UserExecutionGate<CharacterId>",
        nameof(TransactionalExecutionGate),
    };

    [Theory]
    [MemberData(nameof(Gates))]
    public async Task Taking_a_gate_off_the_executor_throws_and_reports_it(string name)
    {
        var gate = Gate.Make(name);
        var seen = ViolationCapture.Attach();

        var thrown = await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await (await gate.AcquireAsync(CancellationToken.None)).DisposeAsync());

        Assert.Contains("must happen inside a gameplay job", thrown.Message, StringComparison.Ordinal);
        var violation = Assert.Single(seen);
        Assert.Equal(GameplayGateAssertion.OffExecutorKind, violation.Kind);
        Assert.Contains(gate.Display, violation.Operation, StringComparison.Ordinal);
        Assert.Equal(0, gate.Tracked());
    }

    [Theory]
    [MemberData(nameof(Gates))]
    public async Task On_the_executor_an_uncontended_gate_is_taken_and_released(string name)
    {
        var gate = Gate.Make(name);
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var seen = new ConcurrentQueue<GameplayAssertionViolation>();

        await executor.RunAsync(ExecutionTest.Work(async ct =>
        {
            ViolationCapture.Attach(seen);
            await (await gate.AcquireAsync(ct)).DisposeAsync();
            await (await gate.AcquireAsync(ct)).DisposeAsync();
        }), CancellationToken.None).AsTask().WaitAsync(Patience);

        Assert.Empty(seen);
        Assert.Equal(0, gate.Tracked());
    }

    [Theory]
    [MemberData(nameof(Gates))]
    public async Task On_the_executor_a_contended_gate_throws_at_once_and_fails_the_job_even_if_swallowed(string name)
    {
        var gate = Gate.Make(name);
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var seen = new ConcurrentQueue<GameplayAssertionViolation>();
        // A job that ends without releasing its lease, so the next job finds the gate taken.
        var leaked = await executor.RunAsync(ExecutionTest.Work(ct => gate.AcquireAsync(ct)), CancellationToken.None)
            .AsTask().WaitAsync(Patience);
        GameplayGateWaitException? caught = null;

        var failure = await Assert.ThrowsAsync<GameplayGateWaitException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(async ct =>
            {
                ViolationCapture.Attach(seen);
                try
                {
                    await gate.AcquireAsync(ct);
                }
                catch (GameplayGateWaitException exception)
                {
                    caught = exception;
                }
            }), CancellationToken.None).AsTask().WaitAsync(Patience));

        Assert.Same(caught, failure);
        Assert.Contains(gate.Display, failure.Gate, StringComparison.Ordinal);
        var violation = Assert.Single(seen);
        Assert.Equal(GameplayGateAssertion.WaitKind, violation.Kind);
        await leaked.DisposeAsync();
    }

    [Theory]
    [MemberData(nameof(Gates))]
    public async Task A_wait_violation_leaks_nothing_and_the_gate_opens_once_its_holder_releases(string name)
    {
        var gate = Gate.Make(name);
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var leaked = await executor.RunAsync(ExecutionTest.Work(ct => gate.AcquireAsync(ct)), CancellationToken.None)
            .AsTask().WaitAsync(Patience);
        await Assert.ThrowsAsync<GameplayGateWaitException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(async ct => await gate.AcquireAsync(ct)), CancellationToken.None)
                .AsTask().WaitAsync(Patience));

        await executor.RunAsync(ExecutionTest.Work(async ct =>
        {
            await leaked.DisposeAsync();
            await (await gate.AcquireAsync(ct)).DisposeAsync();
        }), CancellationToken.None).AsTask().WaitAsync(Patience);

        Assert.Equal(0, gate.Tracked());
    }

    [Theory]
    [MemberData(nameof(Gates))]
    public async Task A_cancelled_caller_gets_cancellation_and_no_violation(string name)
    {
        var gate = Gate.Make(name);
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var seen = ViolationCapture.Attach();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await gate.AcquireAsync(cancelled.Token));
        await executor.RunAsync(ExecutionTest.Work(async _ =>
        {
            ViolationCapture.Attach(seen);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await gate.AcquireAsync(cancelled.Token));
        }), CancellationToken.None).AsTask().WaitAsync(Patience);

        Assert.Empty(seen);
        Assert.Equal(0, gate.Tracked());
    }

    [Fact]
    public async Task Borrowing_inside_the_job_that_holds_the_lease_is_not_a_violation()
    {
        var gate = new SelectedSessionTransitionGate();
        var account = new AccountId(11);
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var seen = new ConcurrentQueue<GameplayAssertionViolation>();

        await executor.RunAsync(ExecutionTest.Work(async ct =>
        {
            ViolationCapture.Attach(seen);
            await using var held = await gate.AcquireAsync(account, UserAccessMode.Exclusive, ct);
            await (await gate.BorrowOrAcquireSharedAsync(account, ct)).DisposeAsync();
            await (await gate.BorrowOrAcquireExclusiveAsync(account, ct)).DisposeAsync();
        }), CancellationToken.None).AsTask().WaitAsync(Patience);

        Assert.Empty(seen);
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task A_borrow_frame_carried_off_the_executor_is_refused()
    {
        var gate = new SelectedSessionTransitionGate();
        var account = new AccountId(12);
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var seen = new ConcurrentQueue<GameplayAssertionViolation>();
        Exception? refused = null;

        await executor.RunAsync(ExecutionTest.Work(async ct =>
        {
            ViolationCapture.Attach(seen);
            await using var held = await gate.AcquireAsync(account, UserAccessMode.Exclusive, ct);
            refused = Record.Exception(() =>
                GameplayJob.RunDetached(() => gate.BorrowOrAcquireSharedAsync(account, ct)));
        }), CancellationToken.None).AsTask().WaitAsync(Patience);

        Assert.IsType<GameplayOffExecutorException>(refused);
        var violation = Assert.Single(seen);
        Assert.Equal(GameplayGateAssertion.OffExecutorKind, violation.Kind);
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task Acquiring_many_contended_on_the_second_account_throws_and_releases_the_first()
    {
        var gate = new SelectedSessionTransitionGate();
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var seen = new ConcurrentQueue<GameplayAssertionViolation>();
        var leaked = await executor.RunAsync(ExecutionTest.Work(ct =>
            gate.AcquireAsync(new AccountId(2), UserAccessMode.Exclusive, ct)), CancellationToken.None)
            .AsTask().WaitAsync(Patience);

        await Assert.ThrowsAsync<GameplayGateWaitException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(async ct =>
            {
                ViolationCapture.Attach(seen);
                await gate.AcquireManyExclusiveAsync([new AccountId(1), new AccountId(2)], ct);
            }), CancellationToken.None).AsTask().WaitAsync(Patience));
        await executor.RunAsync(ExecutionTest.Work(async ct =>
            await (await gate.AcquireAsync(new AccountId(1), UserAccessMode.Exclusive, ct)).DisposeAsync()),
            CancellationToken.None).AsTask().WaitAsync(Patience);

        Assert.Equal(GameplayGateAssertion.WaitKind, Assert.Single(seen).Kind);
        await leaked.DisposeAsync();
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task The_effect_ledger_gate_cannot_be_taken_off_the_executor()
    {
        using var f = new ShopFixture();
        var ledger = f.Services.GetRequiredService<EffectOperationLedger>();
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await ledger.DispatchAsync(f.Connection, EffectRequest(1), () => ValueTask.CompletedTask, default));

        Assert.Equal(GameplayGateAssertion.OffExecutorKind, Assert.Single(seen).Kind);
    }

    [Fact]
    public async Task A_contended_effect_ledger_gate_fails_the_job_at_once()
    {
        using var f = new ShopFixture();
        var ledger = f.Services.GetRequiredService<EffectOperationLedger>();
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The first request's job ends while its settlement still holds the connection's gate.
        await TestGameplay.RunAsync(f.Services, () =>
        {
            _ = ledger.DispatchAsync(f.Connection, EffectRequest(1), async () =>
            {
                entered.TrySetResult();
                await never.Task;
            }, default).AsTask();
            return Task.CompletedTask;
        }).WaitAsync(Patience);
        await entered.Task.WaitAsync(Patience);
        var seen = new ConcurrentQueue<GameplayAssertionViolation>();

        await Assert.ThrowsAsync<GameplayGateWaitException>(() => TestGameplay.RunAsync(f.Services, async () =>
        {
            ViolationCapture.Attach(seen);
            await ledger.DispatchAsync(f.Connection, EffectRequest(2), () => ValueTask.CompletedTask, default);
        }).WaitAsync(Patience));

        Assert.Equal(GameplayGateAssertion.WaitKind, Assert.Single(seen).Kind);
    }

    [Fact]
    public async Task The_joust_tick_gate_cannot_be_taken_off_the_executor()
    {
        var service = Joust();
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () => await JoustTickAsync(service));

        Assert.Equal(GameplayGateAssertion.OffExecutorKind, Assert.Single(seen).Kind);
    }

    [Fact]
    public async Task A_contended_joust_tick_gate_fails_the_job_at_once()
    {
        var service = Joust();
        var ticks = (SemaphoreSlim)typeof(JoustMaintenanceService)
            .GetField("ticks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(service)!;
        Assert.True(ticks.Wait(0));
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var seen = new ConcurrentQueue<GameplayAssertionViolation>();

        await Assert.ThrowsAsync<GameplayGateWaitException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(async _ =>
            {
                ViolationCapture.Attach(seen);
                await JoustTickAsync(service);
            }), CancellationToken.None).AsTask().WaitAsync(Patience));

        Assert.Equal(GameplayGateAssertion.WaitKind, Assert.Single(seen).Kind);
        ticks.Release();
    }

    private static UseStackableRequest EffectRequest(ushort sequence)
    {
        var body = new byte[11];
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), 69);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7), 1000);
        var metadata = new GameMessageMetadata(1, 0x002C, 0, sequence)
        {
            InboundOrdinal = sequence,
            SequenceIsFresh = true,
        }.WithRawBody(body);
        return new UseStackableRequest(metadata, new(InventoryContainerType.Main, 69), 0, 1000);
    }

    // The gate is the tick body's first statement, so the collaborators are never reached.
    private static JoustMaintenanceService Joust() =>
        new(null!, null!, null!, null!, TimeProvider.System, null!, NullLogger<JoustMaintenanceService>.Instance);

    private static ValueTask JoustTickAsync(JoustMaintenanceService service) =>
        (ValueTask)typeof(JoustMaintenanceService)
            .GetMethod("TickCoreAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(service, [CancellationToken.None, true])!;

    /// <summary>
    /// Ruling 2: the concurrency slot is the producer's own, held off the executor while it waits on a job,
    /// so instead of a gate assertion it gets the reverse one. (The event group gate's reverse assertion is
    /// <c>DomainEventDispatchTests.Draining_from_inside_a_job_fails_at_once_as_a_program_error</c>.)
    /// </summary>
    [Fact]
    public async Task Taking_a_scheduled_task_slot_on_the_executor_is_a_self_wait()
    {
        var queue = new CountingQueue();
        var ran = 0;
        var registration = new ScheduledTaskRegistration("slot.probe", new IntervalTaskSchedule(TimeSpan.FromDays(1)),
            (_, _) =>
            {
                Interlocked.Increment(ref ran);
                return ValueTask.CompletedTask;
            }) { MissedRun = ScheduledTaskMissedRunPolicy.RunOnce };
        using var services = new ServiceCollection().BuildServiceProvider();
        using var scheduler = new ScheduledTaskHostedService([registration],
            services.GetRequiredService<IServiceScopeFactory>(), queue, TimeProvider.System,
            new ScheduledTaskSchedulerOptions(), NullLogger<ScheduledTaskHostedService>.Instance);
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;

        var failure = await Assert.ThrowsAsync<GameplaySelfWaitException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(async ct =>
            {
                // The attempt inherits this job's flow, as a callback started from a job would.
                await scheduler.StartAsync(ct);
                var deadline = Stopwatch.StartNew();
                while (GameplayJob.Active!.ProgramError is null && queue.Calls == 0 && deadline.Elapsed < Patience)
                    await Task.Delay(10, ct);
                await scheduler.StopAsync(CancellationToken.None);
            }), CancellationToken.None).AsTask().WaitAsync(Patience * 2));

        Assert.Contains("scheduled task concurrency slot", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, queue.Calls);
        Assert.Equal(0, ran);
    }

    private sealed class CountingQueue : IGameplayQueue
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(default(T)!);
        }

        public ValueTask RunAsync(GameplayWork work, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record Gate(
        string Display,
        Func<CancellationToken, ValueTask<IAsyncDisposable>> AcquireAsync,
        Func<int> Tracked)
    {
        public static Gate Make(string name)
        {
            switch (name)
            {
                case nameof(OnlineUserExecutionGate):
                {
                    var gate = new OnlineUserExecutionGate();
                    var user = OnlineUserId.New();
                    return new(name, ct => gate.AcquireAsync(user, UserAccessMode.Exclusive, ct), () => gate.TrackedEntryCount);
                }
                case nameof(AccountExecutionGate):
                {
                    var gate = new AccountExecutionGate();
                    return new(name, ct => gate.AcquireAsync(new AccountId(5), UserAccessMode.Exclusive, ct),
                        () => gate.TrackedEntryCount);
                }
                case nameof(SelectedSessionTransitionGate):
                {
                    var gate = new SelectedSessionTransitionGate();
                    return new(name, ct => gate.AcquireAsync(new AccountId(6), UserAccessMode.Exclusive, ct),
                        () => gate.TrackedEntryCount);
                }
                case "UserExecutionGate<CharacterId>":
                {
                    var gate = new UserExecutionGate<CharacterId>();
                    return new("UserExecutionGate", ct => gate.AcquireAsync(new CharacterId(7), UserAccessMode.Exclusive, ct),
                        () => gate.TrackedEntryCount);
                }
                case nameof(TransactionalExecutionGate):
                {
                    var gate = new TransactionalExecutionGate();
                    return new(name, gate.AcquireAsync, () => 0);
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(name), name, null);
            }
        }
    }
}

/// <summary>
/// Collects the violations reported on the current flow. A job does not inherit the test's flow, so a
/// job body attaches the queue itself.
/// </summary>
internal static class ViolationCapture
{
    private static readonly AsyncLocal<ConcurrentQueue<GameplayAssertionViolation>?> Current = new();
    private static readonly IDisposable Subscription = DiagnosticListener.AllListeners.Subscribe(new Sources());

    public static ConcurrentQueue<GameplayAssertionViolation> Attach(ConcurrentQueue<GameplayAssertionViolation>? into = null)
    {
        _ = Subscription;
        into ??= new ConcurrentQueue<GameplayAssertionViolation>();
        Current.Value = into;
        return into;
    }

    private sealed class Sources : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == GameplayGateAssertion.DiagnosticSourceName) value.Subscribe(new Events());
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class Events : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is GameplayAssertionViolation violation) Current.Value?.Enqueue(violation);
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
