using System.Collections.Concurrent;
using System.Reflection;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Altar;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Execution;

[Collection("Scheduler")]
public sealed class WorldMaintenanceFailureIsolationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("altar")]
    [InlineData("room-mechanism")]
    public async Task A_failed_scan_retries_on_its_own_deadline_without_an_external_wakeup(string source)
    {
        var label = "timer:DungeonDeadlineScheduler/" + source;
        FailedScan? fault = null;
        await using var host = new TimerSourceHost(wrapQueue: queue => fault = new(queue, label));
        await host.StartAsync(host.Hosted("DungeonDeadlineScheduler"));
        await Until(() => fault!.Failed && host.Clock.ActiveTimers > 0);
        // No Wake() here: that would hide the infinite-wait regression.
        host.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await host.Queue.WaitForAsync(x => x.CommandType == label && x.Completed, Patience));
        Assert.True(fault!.Failed);
    }

    [Fact]
    public async Task An_uncertain_altar_publication_ends_its_job_and_the_next_run_still_advances()
    {
        var sessions = TestSessions.Directory(out var state);
        var first = sessions.InWorld(state, new(9101), new(9101), "isolated-altar-1");
        var second = sessions.InWorld(state, new(9102), new(9102), "isolated-altar-2");
        var runs = TestParties.Runs();
        var registry = new DungeonRunDeliveryRegistry();
        var altars = DispatchProxy.Create<IAltarDungeonDomainService, AltarTrace>();
        var trace = (AltarTrace)(object)altars;
        var projections = new AltarProjections();
        await using var host = new TimerSourceHost(services =>
        {
            services.AddSingleton<ISelectedSessionDomainService>(sessions);
            services.AddDungeonRunsAs(runs);
            services.AddDungeonRunViews();
            services.AddSingleton<IDungeonRunProjectionDomainService>(registry);
            services.AddSingleton(altars);
            services.AddSingleton<ICommandProjections>(projections);
        });
        trace.Transactions = host.Services.GetRequiredService<IDomainTransactionAccessor>();
        await TestGameplay.RunAsync(host.Services, () =>
        {
            foreach (var member in new[] { first, second })
            {
                var run = Prepare(runs, member.CharacterId, host.Clock.GetUtcNow(), mechanisms: false);
                registry.Register(run.RunId, [member]);
                trace.States.Add(run.RunId, new(run.RunId,
                    new(144, AltarDungeonKind.Endless, 1, 1, 1, 0, [], new(0, 0, 0, 0, [], [], [], [], ""), 3),
                    [], host.Clock.GetUtcNow(), run.RoomVersion, 12345) { NextDeadline = host.Clock.GetUtcNow() });
            }
            projections.FailedRun = trace.States.Keys.First();
            return Task.CompletedTask;
        });
        await host.StartAsync(host.Hosted("DungeonDeadlineScheduler"));
        await Until(() => projections.Seen.Count == 2 && host.Queue.Ran.Count(x => x.CommandType == "timer:DungeonDeadlineScheduler/altar/deadline") == 2);
        Assert.True(sessions.StandingOf(first)!.Fenced);
        Assert.False(sessions.StandingOf(second)!.Fenced);
        Assert.Equal(2, trace.Advanced.Count);
        Assert.Equal(2, trace.Advanced.Select(x => x.Job).Distinct().Count());
        Assert.Contains(host.Queue.Ran, x => x.CommandType == "timer:DungeonDeadlineScheduler/altar/deadline" && !x.Completed);
        Assert.Contains(host.Queue.Ran, x => x.CommandType == "timer:DungeonDeadlineScheduler/altar/deadline" && x.Completed);
        Assert.All(trace.Advanced, x => Assert.Equal(DomainTransactionCompletion.Committed, x.Transaction.Completion));
        Assert.Equal(2, trace.States.Count);
        Assert.All(trace.States.Values, value => Assert.Equal(1, value.Generation));
    }

    [Fact]
    public async Task A_room_subscriber_cleanup_failure_is_isolated_and_the_undelivered_snapshot_retries_without_wake()
    {
        var runs = TestParties.Runs();
        var trace = new RoomTrace();
        await using var host = new TimerSourceHost(services =>
        {
            services.AddDungeonRunsAs(runs);
            services.AddDungeonRunViews();
            services.AddSingleton<ISelectedSessionSnapshotQuery>(new EmptySessions());
            services.AddSingleton(trace);
            services.AddRuntimeEventSubscriber<DungeonRoomMechanismsChanged, RoomSubscriber>();
        });
        await TestGameplay.RunAsync(host.Services, () =>
        {
            trace.FailedRun = Prepare(runs, new(9201), host.Clock.GetUtcNow(), mechanisms: true).RunId;
            _ = Prepare(runs, new(9202), host.Clock.GetUtcNow(), mechanisms: true);
            return Task.CompletedTask;
        });
        await host.StartAsync(host.Hosted("DungeonDeadlineScheduler"));
        await Until(() => trace.Seen.Count == 2 && host.Clock.ActiveTimers > 0);
        Assert.Equal(2, trace.Seen.Select(x => x.Job).Distinct().Count());
        Assert.Contains(host.Queue.Ran, x => x.CommandType == "timer:DungeonDeadlineScheduler/room-mechanism/room" && !x.Completed);
        Assert.Contains(host.Queue.Ran, x => x.CommandType == "timer:DungeonDeadlineScheduler/room-mechanism/room" && x.Completed);
        host.Clock.Advance(TimeSpan.FromSeconds(2));
        await Until(() => trace.Seen.Count == 3);
        Assert.Equal(2, trace.Seen.Count(x => x.Run == trace.FailedRun));
        Assert.Single(trace.Seen, x => x.Run != trace.FailedRun);
        Assert.Equal(1, trace.CleanupFailures);
    }

    private static DungeonRunState Prepare(IDungeonRunDomainService runs, CharacterId character, DateTimeOffset now, bool mechanisms)
    {
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(144, 0, 1, 1, cell, cell, 12345, 9,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(12345) });
        var seed = new DungeonCellSeed(cell, 12345, 1, [new(1, new(123, 1, 0), false, false, [])], [])
        { Mechanisms = mechanisms ? [new(DungeonRoomMechanismKind.Elevator, TimeSpan.FromMinutes(1), 4)] : [] };
        var run = runs.PreparePartyEntry([new(new(character.Value), character, 0, true)], layout, 0, seed, DungeonRunId.New(), now).Run;
        runs.TryMarkParticipantReady(character, run.RunId, run.RoomVersion);
        return run;
    }

    private static async Task Until(Func<bool> condition)
    {
        using var stop = new CancellationTokenSource(Patience);
        while (!condition()) await Task.Delay(5, stop.Token);
    }

    private sealed class EmptySessions : ISelectedSessionSnapshotQuery
    { public IReadOnlyList<SelectedSessionIdentity> Snapshot() => []; }

    private sealed class RoomTrace
    {
        internal DungeonRunId FailedRun;
        internal int CleanupFailures;
        internal ConcurrentQueue<(DungeonRunId Run, Guid Job)> Seen { get; } = new();
    }
    private sealed class RoomSubscriber(RoomTrace trace) : IDomainEventSubscriber<DungeonRoomMechanismsChanged>, IAsyncDisposable
    {
        private DungeonRunId run;
        public ValueTask HandleAsync(DomainEventEnvelope<DungeonRoomMechanismsChanged> envelope, CancellationToken token)
        {
            run = envelope.Payload.View.RunId;
            trace.Seen.Enqueue((run, GameplayJob.Current!.Value.Id));
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync() => run == trace.FailedRun && Interlocked.CompareExchange(ref trace.CleanupFailures, 1, 0) == 0
            ? ValueTask.FromException(new IOException("Injected room subscriber scope cleanup failure")) : ValueTask.CompletedTask;
    }

    public class AltarTrace : DispatchProxy
    {
        internal Dictionary<DungeonRunId, AltarDungeonState> States { get; } = [];
        internal ConcurrentQueue<(Guid Job, DomainTransaction Transaction)> Advanced { get; } = new();
        internal IDomainTransactionAccessor? Transactions;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IAltarDungeonDomainService.Find) => States.GetValueOrDefault((DungeonRunId)args![0]!),
            nameof(IAltarDungeonDomainService.Pending) => States.Values.Where(x => x.NextDeadline is not null)
                .Select(x => new AltarDeadline(x.RunId, x.RoomVersion, x.Generation, x.NextDeadline!.Value)).ToArray(),
            nameof(IAltarDungeonDomainService.AdvanceAsync) => Advance((DungeonRunId)args![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };
        private ValueTask<AltarActionResult?> Advance(DungeonRunId id)
        {
            Advanced.Enqueue((GameplayJob.Current!.Value.Id, Assert.IsType<DomainTransaction>(Transactions!.Current)));
            var changed = States[id] with { Generation = States[id].Generation + 1, NextDeadline = null };
            States[id] = changed;
            return ValueTask.FromResult<AltarActionResult?>(new(changed));
        }
    }
    private sealed class AltarProjections : ICommandProjections
    {
        internal DungeonRunId FailedRun;
        internal ConcurrentQueue<DungeonRunId> Seen { get; } = new();
        public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience, T notification, CancellationToken token)
            where T : class, INotification<T>
        {
            var action = Assert.IsType<AltarStateChanged>(notification).Action;
            Seen.Enqueue(action.State.RunId);
            return action.State.RunId == FailedRun ? ValueTask.FromException<NotificationDelivery>(new IOException("Injected altar composer failure"))
                : ValueTask.FromResult(new NotificationDelivery([]));
        }
    }
    private sealed class FailedScan(IGameplayQueue inner, string label) : IGameplayQueue
    {
        private int failed;
        internal bool Failed => Volatile.Read(ref failed) != 0;
        public ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken token) => work.CommandType == label && Interlocked.CompareExchange(ref failed, 1, 0) == 0
            ? inner.RunAsync(work with { Body = _ => ValueTask.FromException<T>(new IOException("Injected first scan failure")) }, token)
            : inner.RunAsync(work, token);
        public ValueTask RunAsync(GameplayWork work, CancellationToken token) => inner.RunAsync(work, token);
    }
}
