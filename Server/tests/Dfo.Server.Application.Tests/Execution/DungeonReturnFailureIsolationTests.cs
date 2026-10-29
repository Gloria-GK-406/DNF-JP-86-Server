using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Logging;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Execution;

[Collection("Scheduler")]
public sealed class DungeonReturnFailureIsolationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_ordinary_return_member_does_not_block_the_next_member_or_next_run_and_the_next_tick_does_not_replay(bool cleared)
    {
        var sessions = TestSessions.Directory(out var online);
        var members = Enumerable.Range(1, 4).Select(index => sessions.InWorld(online,
            new(9300 + index), new(9300 + index), "ordinary-return-" + index)).ToArray();
        var runs = TestParties.Runs();
        var registry = new DungeonRunDeliveryRegistry();
        var views = new ReturnViews();
        foreach (var session in members)
            views.Values.Add(session.CharacterId, await ReturnViewAsync(session));
        RecordedOutbound? output = null;
        await using var host = new TimerSourceHost(services =>
        {
            services.AddSingleton(online);
            services.AddSingleton<ISelectedSessionDomainService>(sessions);
            services.AddSingleton<IDungeonRunDomainService>(runs);
            services.AddDungeonRunViews();
            services.AddSingleton<IDungeonRunProjectionDomainService>(registry);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(views);
            services.AddSingleton<IWeaknessDomainService>(new ReturnWeakness());
            services.AddSingleton<ITownMovementDomainService>(ProtocolAlgorithmFixture.Default<ITownMovementDomainService>());
            services.AddSingleton<ICommandProjections>(new ReturnProjections());
            output = RecordedOutbound.AddTo(services);
        });
        var outbound = Assert.IsType<RecordedOutbound>(host.Services.GetRequiredService<RecordedOutbound>());
        Assert.Same(output, outbound);
        var returnedRuns = new List<DungeonRunId>();
        await TestGameplay.RunAsync(host.Services, () =>
        {
            var runtime = host.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>();
            foreach (var session in members)
            {
                Assert.True(online.TryGetByConnection(session.ConnectionId, out var user));
                outbound.Connections.Add(user!.Connection);
                runtime.Begin(session, new CharacterPositionState((byte)(session.CharacterId.Value - 9300), 1, 100, 200, 0), 100);
            }
            for (var group = 0; group < 2; group++)
            {
                var cohort = members.Skip(group * 2).Take(2).ToArray();
                returnedRuns.Add(Prepare(runs, cohort, host.Clock.GetUtcNow(), cleared));
                registry.Register(returnedRuns[^1], cohort);
            }
            return Task.CompletedTask;
        });
        outbound.Encoder.Fail<UserAreaNotification>(members[0].ConnectionId);
        await host.StartAsync(host.Hosted("DungeonPartyWipeService"));
        await Until(() => host.Clock.ActiveTimers > 0);
        host.Clock.Advance(TimeSpan.FromMilliseconds(250));
        await Until(() => host.Queue.Ran.Count(job => job.CommandType == "timer:dungeon-return-member") == 4);
        Assert.Equal(2, host.Queue.Ran.Count(job => job.CommandType == "timer:dungeon-return-run" && job.Completed));
        Assert.Single(host.Queue.Ran, job => job.CommandType == "timer:dungeon-return-member" && !job.Completed);
        Assert.Equal(3, host.Queue.Ran.Count(job => job.CommandType == "timer:dungeon-return-member" && job.Completed));
        Assert.Equal(members, views.Reads.Select(read => read.Session));
        Assert.Equal(4, views.Reads.Select(read => read.Job).Distinct().Count());
        Assert.All(views.Reads, read => Assert.Equal(CancellationToken.None, read.Token));
        Assert.DoesNotContain(outbound.MessagesFor(members[0].ConnectionId), message => message is UserAreaNotification);
        foreach (var session in members.Skip(1))
        {
            var messages = outbound.MessagesFor(session.ConnectionId).ToArray();
            Assert.Single(messages.OfType<UserAreaNotification>());
            Assert.Equal((byte)37, Assert.Single(messages.OfType<WeaknessProjectionNotification>()).Value);
            var frames = outbound.Entries.Where(entry => entry.Connection.ConnectionId == session.ConnectionId).SelectMany(entry => entry.Messages).ToArray();
            Assert.True(Array.FindIndex(frames, message => message is UserAreaNotification)
                < Array.FindIndex(frames, message => message is WeaknessProjectionNotification));
        }
        TestGameplay.Run(host.Services, () =>
        {
            foreach (var session in members) Assert.Null(runs.Find(session.CharacterId));
            foreach (var run in returnedRuns) Assert.Empty(registry.Find(run));
        });
        var registrations = outbound.Entries.Count;
        var scans = host.Queue.Ran.Count(job => job.CommandType == "timer:DungeonPartyWipeService");
        host.Clock.Advance(TimeSpan.FromMilliseconds(250));
        await Until(() => host.Queue.Ran.Count(job => job.CommandType == "timer:DungeonPartyWipeService") > scans);
        Assert.Equal(4, host.Queue.Ran.Count(job => job.CommandType == "timer:dungeon-return-member"));
        Assert.Equal(registrations, outbound.Entries.Count);
        Assert.Equal(4, views.Reads.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_after_the_ordinary_run_ends_does_not_skip_committed_member_returns(bool cleared)
    {
        using var producer = new CancellationTokenSource();
        var errors = new RecordingExecutionLogger<GameplayExecutor>();
        var sessions = TestSessions.Directory(out var online);
        var members = Enumerable.Range(1, 2).Select(index => sessions.InWorld(online,
            new(9400 + index), new(9400 + index), "cancelled-ordinary-return-" + index)).ToArray();
        var runs = TestParties.Runs();
        var registry = new DungeonRunDeliveryRegistry();
        var views = new ReturnViews();
        foreach (var session in members)
            views.Values.Add(session.CharacterId, await ReturnViewAsync(session));
        var tracedRuns = ProtocolAlgorithmFixture.Decorate<IDungeonRunDomainService>(runs, (method, arguments) =>
        {
            if (method.Name != (cleared ? nameof(IDungeonRunDomainService.TryEndClearReturnAsync) : nameof(IDungeonRunDomainService.TryEndPartyWipeAsync)))
                return (false, null);
            var runId = (DungeonRunId)arguments[0]!; var now = (DateTimeOffset)arguments[1]!;
            var ended = cleared ? runs.TryEndClearReturn(runId, now) : runs.TryEndPartyWipe(runId, now);
            Assert.NotNull(ended);
            producer.Cancel();
            return (true, ValueTask.FromResult(ended));
        });
        await using var host = new TimerSourceHost(services =>
        {
            services.AddSingleton<ILogger<GameplayExecutor>>(errors);
            services.AddSingleton(online);
            services.AddSingleton<ISelectedSessionDomainService>(sessions);
            services.AddSingleton(tracedRuns);
            services.AddSingleton<IDungeonRunProjectionDomainService>(registry);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(views);
            services.AddSingleton<IWeaknessDomainService>(new ReturnWeakness());
            services.AddSingleton<ITownMovementDomainService>(ProtocolAlgorithmFixture.Default<ITownMovementDomainService>());
            services.AddSingleton<ICommandProjections>(new ReturnProjections());
            RecordedOutbound.AddTo(services);
        });
        var outbound = host.Services.GetRequiredService<RecordedOutbound>();
        DungeonRunId run = default;
        TestGameplay.Run(host.Services, () =>
        {
            foreach (var session in members)
            {
                Assert.True(online.TryGetByConnection(session.ConnectionId, out var user)); outbound.Connections.Add(user!.Connection);
                host.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().Begin(session, new(1, 1, 100, 200, 0), 100);
            }
            run = Prepare(runs, members, host.Clock.GetUtcNow(), cleared); registry.Register(run, members);
        });
        var source = Assert.IsType<Dfo.Server.Protocol.Game.CharacterDeath.DungeonPartyWipeService>(host.Hosted("DungeonPartyWipeService"));
        await source.ReturnAsync(run, producer.Token, cleared);
        Assert.True(producer.IsCancellationRequested);
        Assert.All(errors.Entries, entry => Assert.True(entry.Level < LogLevel.Error, entry.Exception?.ToString()));
        Assert.Equal(2, host.Queue.Ran.Count(job => job.CommandType == "timer:dungeon-return-member" && job.Completed));
        Assert.DoesNotContain(host.Queue.Ran, job => !job.Completed);
        Assert.All(views.Reads, read => Assert.Equal(CancellationToken.None, read.Token));
        foreach (var session in members)
            Assert.Equal((byte)37, Assert.Single(outbound.MessagesFor(session.ConnectionId).OfType<WeaknessProjectionNotification>()).Value);
    }

    private static async ValueTask<SelectCharacterResult> ReturnViewAsync(SelectedSessionIdentity session)
    {
        var view = await CharacterEntryInventoryTests.CreateLifecycleResultAsync(session.AccountId, session.CharacterId.Value, 0);
        // Same-town returns include peer USERINFO, whose protocol projection requires public identity facts.
        return view with { CharacterEntry = view.CharacterEntry with { Public = view.CharacterEntry.Public with { Identity = new(0, 0, 0) } } };
    }

    private static DungeonRunId Prepare(DungeonRunDomainService runs, SelectedSessionIdentity[] cohort, DateTimeOffset now, bool cleared)
    {
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(120, 0, 1, 1, cell, cell, 1000, 42,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(1000) });
        var run = runs.PreparePartyEntry(cohort.Select((session, index) => new DungeonRunParticipant(session.AccountId,
            session.CharacterId, (byte)index, index == 0)).ToArray(), layout, 0).Run;
        if (cleared)
        {
            Assert.NotNull(runs.TryOpenClear(cohort[0].CharacterId, run.RunId, run.RoomVersion));
            var free = new ClearRewardCardFace(ClearRewardCardKind.Free, [new(0, null)], 1);
            var paid = new ClearRewardCardFace(ClearRewardCardKind.Paid, [new(0, null)], 2);
            foreach (var session in cohort)
            {
                runs.FixClearRewardCards(session.CharacterId, run.RunId, free, paid, 3);
                Assert.NotNull(runs.CommitSettlement(runs.TryClaimSettlement(session.CharacterId, run.RunId)!));
                Assert.NotNull(runs.TryOpenCardLayout(session.CharacterId, run.RunId));
            }
            runs.ScheduleClearReturn(cohort[0].CharacterId, run.RunId, now.AddSeconds(-61));
        }
        else
        {
            foreach (var session in cohort)
            {
                Assert.NotNull(runs.TryMarkParticipantReady(session.CharacterId, run.RunId, run.RoomVersion));
                Assert.NotNull(runs.TryMarkParticipantDead(session.CharacterId, run.RunId, run.RoomVersion));
            }
            _ = runs.FindExpiredPartyWipes(now.AddSeconds(-11));
        }
        return run.RunId;
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Patience);
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    private sealed class ReturnViews : ISelectedCharacterCurrentViewDomainService
    {
        internal Dictionary<CharacterId, SelectCharacterResult> Values { get; } = [];
        internal List<(SelectedSessionIdentity Session, Guid Job, CancellationToken Token)> Reads { get; } = [];
        public ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken token)
        {
            Reads.Add((session, GameplayJob.Current!.Value.Id, token));
            return ValueTask.FromResult<SelectCharacterResult?>(Values[session.CharacterId]);
        }
    }
    private sealed class ReturnWeakness : IWeaknessDomainService, IWeakness
    {
        public IWeakness For(CharacterId id) => this;
        public byte Value => 37;
        public TimeSpan RecoveryInterval => TimeSpan.FromMinutes(1);
        public IReadOnlyList<CharacterId> AdvanceRecovery() => [];
        public ValueTask<int> RecoveryGoldCost(CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask SetWeakness() => throw new NotSupportedException();
        public void Recover() => throw new NotSupportedException();
        public void PauseRecovery() { }
        public void ResumeRecovery() { }
    }
    private sealed class ReturnProjections : ICommandProjections
    {
        public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience, T notification, CancellationToken ct = default)
            where T : class, INotification<T> => ValueTask.FromResult(new NotificationDelivery([]));
    }
}
