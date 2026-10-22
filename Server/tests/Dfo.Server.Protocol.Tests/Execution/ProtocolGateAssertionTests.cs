using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TitleBook;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Execution;

/// <summary>
/// Plan transition rule 2 for the Protocol gates: off the executor taking one throws, and on the executor
/// a gate that would have to wait fails the job at once. Removed with the gates in T-005.
/// </summary>
/// <remarks>A counterexample class the zero-violation evidence subtracts: 14 violations per run.</remarks>
public sealed class ProtocolGateAssertionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    public static TheoryData<string> DerivedGates => new()
    {
        nameof(TownAreaProjectionGate),
        nameof(DungeonProjectionGate),
        nameof(BossTowerProjectionGate),
        nameof(BossTowerEntryGate),
        nameof(IllusionTowerProjectionGate),
        nameof(IllusionTowerEntryGate),
    };

    [Theory]
    [MemberData(nameof(DerivedGates))]
    public async Task A_projection_or_entry_gate_cannot_be_taken_off_the_executor(string name)
    {
        var seen = Capture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () => await (name switch
        {
            nameof(TownAreaProjectionGate) => new TownAreaProjectionGate().AcquireAsync(new TownAreaKey(1, 2, 3), UserAccessMode.Exclusive, default),
            nameof(DungeonProjectionGate) => new DungeonProjectionGate().AcquireAsync(new DungeonRunId(Guid.NewGuid()), UserAccessMode.Exclusive, default),
            nameof(BossTowerProjectionGate) => new BossTowerProjectionGate().AcquireAsync(Guid.NewGuid(), UserAccessMode.Exclusive, default),
            nameof(BossTowerEntryGate) => new BossTowerEntryGate().AcquireAsync(new CharacterId(4), UserAccessMode.Exclusive, default),
            nameof(IllusionTowerProjectionGate) => new IllusionTowerProjectionGate().AcquireAsync(Guid.NewGuid(), UserAccessMode.Exclusive, default),
            nameof(IllusionTowerEntryGate) => new IllusionTowerEntryGate().AcquireAsync(new CharacterId(5), UserAccessMode.Exclusive, default),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        }));

        var violation = Assert.Single(seen);
        Assert.Equal(GameplayGateAssertion.OffExecutorKind, violation.Kind);
        Assert.Contains(name, violation.Operation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_title_projection_gate_cannot_be_taken_off_the_executor()
    {
        var seen = Capture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await new AchievementTitleProjectionGate().EnterAsync(new CharacterId(1), default));

        Assert.Equal(GameplayGateAssertion.OffExecutorKind, Assert.Single(seen).Kind);
    }

    [Fact]
    public async Task On_the_executor_the_title_projection_gate_is_taken_released_and_never_waited_on()
    {
        var gate = new AchievementTitleProjectionGate();
        await using var executor = Executor();
        var seen = new ConcurrentQueue<GameplayAssertionViolation>();
        await RunAsync(executor, async ct =>
        {
            Capture.Attach(seen);
            (await gate.EnterAsync(new CharacterId(1), ct)).Dispose();
            (await gate.EnterAsync(new CharacterId(1), ct)).Dispose();
        });
        Assert.Empty(seen);
        var leaked = await RunAsync(executor, ct => gate.EnterAsync(new CharacterId(1), ct).AsTask());
        GameplayGateWaitException? caught = null;

        var failure = await Assert.ThrowsAsync<GameplayGateWaitException>(() => RunAsync(executor, async ct =>
        {
            Capture.Attach(seen);
            try
            {
                // Same stripe as character 1: 65 % 64.
                await gate.EnterAsync(new CharacterId(65), ct);
            }
            catch (GameplayGateWaitException exception)
            {
                caught = exception;
            }
        }));

        Assert.Same(caught, failure);
        Assert.Equal(GameplayGateAssertion.WaitKind, Assert.Single(seen).Kind);
        leaked.Dispose();
        await RunAsync(executor, async ct => (await gate.EnterAsync(new CharacterId(1), ct)).Dispose());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Capture.Attach(seen);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await gate.EnterAsync(new CharacterId(1), cancelled.Token));
        Assert.Single(seen);
    }

    [Fact]
    public async Task Party_participant_gates_cannot_be_taken_off_the_executor()
    {
        var seen = Capture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await new PartyProjectionStreamRegistry().EnterParticipantsAsync([Session(1)], default));

        Assert.Equal(GameplayGateAssertion.OffExecutorKind, Assert.Single(seen).Kind);
    }

    [Fact]
    public async Task A_contended_participant_gate_throws_and_releases_the_ones_already_taken()
    {
        var registry = new PartyProjectionStreamRegistry();
        await using var executor = Executor();
        var seen = new ConcurrentQueue<GameplayAssertionViolation>();
        var leaked = await RunAsync(executor, ct => registry.EnterParticipantsAsync([Session(2)], ct).AsTask());

        await Assert.ThrowsAsync<GameplayGateWaitException>(() => RunAsync(executor, async ct =>
        {
            Capture.Attach(seen);
            await registry.EnterParticipantsAsync([Session(2), Session(1)], ct);
        }));
        await RunAsync(executor, async ct => (await registry.EnterParticipantsAsync([Session(1)], ct)).Dispose());

        Assert.Equal(GameplayGateAssertion.WaitKind, Assert.Single(seen).Kind);
        leaked.Dispose();
        await RunAsync(executor, async ct => (await registry.EnterParticipantsAsync([Session(1), Session(2)], ct)).Dispose());
    }

    [Fact]
    public async Task The_party_delivery_stream_gate_cannot_be_taken_off_the_executor()
    {
        var delivery = new PartyNotificationDelivery(null!, new PartyProjectionStreamRegistry());
        var seen = Capture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await delivery.DeliverRealtimeAsync(Session(1), Snapshot(7), default));

        Assert.Equal(GameplayGateAssertion.OffExecutorKind, Assert.Single(seen).Kind);
    }

    [Fact]
    public async Task A_contended_party_delivery_stream_gate_fails_the_job_at_once()
    {
        var registry = new PartyProjectionStreamRegistry();
        var delivery = new PartyNotificationDelivery(null!, registry);
        var stream = registry.GetOrAdd(8);
        Assert.True(stream.Gate.Wait(0));
        await using var executor = Executor();
        var seen = new ConcurrentQueue<GameplayAssertionViolation>();

        await Assert.ThrowsAsync<GameplayGateWaitException>(() => RunAsync(executor, async ct =>
        {
            Capture.Attach(seen);
            await delivery.DeliverRealtimeAsync(Session(1), Snapshot(8), ct);
        }));

        Assert.Equal(GameplayGateAssertion.WaitKind, Assert.Single(seen).Kind);
        stream.Gate.Release();
    }

    [Fact]
    public async Task The_weakness_delivery_gate_cannot_be_taken_off_the_executor()
    {
        // The gate is the first statement, so the collaborators are never reached.
        var delivery = new WeaknessProjectionDelivery(null!, null!, null!);
        var seen = Capture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await delivery.SendCurrentAsync(Session(1), default));

        Assert.Equal(GameplayGateAssertion.OffExecutorKind, Assert.Single(seen).Kind);
    }

    [Fact]
    public async Task A_contended_weakness_delivery_gate_fails_the_job_at_once()
    {
        var delivery = new WeaknessProjectionDelivery(null!, null!, null!);
        var gate = (SemaphoreSlim)typeof(WeaknessProjectionDelivery)
            .GetField("deliveryGate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(delivery)!;
        Assert.True(gate.Wait(0));
        await using var executor = Executor();
        var seen = new ConcurrentQueue<GameplayAssertionViolation>();

        await Assert.ThrowsAsync<GameplayGateWaitException>(() => RunAsync(executor, async ct =>
        {
            Capture.Attach(seen);
            await delivery.SendCurrentAsync(Session(1), ct);
        }));

        Assert.Equal(GameplayGateAssertion.WaitKind, Assert.Single(seen).Kind);
        gate.Release();
    }

    private static SelectedSessionIdentity Session(int character) =>
        new(new AccountId(character), new CharacterId(character), new NetworkConnectionId($"gate-{character}"), 1, 1, 10011);

    private static PartySnapshot Snapshot(long generation) =>
        new(1, generation, 1, 1, null!, PartyTitle.Empty, []);

    private static GameplayExecutor Executor() => new(NullLogger<GameplayExecutor>.Instance);

    private static Task RunAsync(GameplayExecutor executor, Func<CancellationToken, Task> body) =>
        executor.RunAsync(new GameplayWork(GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.Connection),
            "test:gate", async ct => await body(ct)), CancellationToken.None).AsTask().WaitAsync(Patience);

    private static Task<T> RunAsync<T>(GameplayExecutor executor, Func<CancellationToken, Task<T>> body) =>
        executor.RunAsync(new GameplayWork<T>(GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.Connection),
            "test:gate", async ct => await body(ct)), CancellationToken.None).AsTask().WaitAsync(Patience);

    private static class Capture
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
}
