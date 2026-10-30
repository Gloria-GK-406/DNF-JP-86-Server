using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Execution;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.World;

[Collection("Scheduler")]
public sealed class DungeonDeadlineSchedulerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private const string Altar = "timer:DungeonDeadlineScheduler/altar";
    private const string Guardian = "timer:DungeonDeadlineScheduler/guardian-altar";
    private const string Room = "timer:DungeonDeadlineScheduler/room-mechanism";

    [Fact]
    public void Two_sources_for_one_kind_are_refused() =>
        Assert.Throws<InvalidOperationException>(() => new DungeonDeadlineScheduler(
            [new IdleSource(DungeonDeadlineKind.Altar), new IdleSource(DungeonDeadlineKind.Altar)],
            null!, TimeProvider.System, NullLogger<DungeonDeadlineScheduler>.Instance));

    [Fact]
    public async Task A_wake_rescans_only_its_own_kind()
    {
        await using var host = new TimerSourceHost();
        await host.StartAsync(host.Hosted(nameof(DungeonDeadlineScheduler)));
        await Until(() => Scans(host, Altar) >= 2 && Scans(host, Guardian) >= 2 && Scans(host, Room) >= 2);
        var (altar, guardian, room) = (Scans(host, Altar), Scans(host, Guardian), Scans(host, Room));

        host.Services.GetRequiredService<IDungeonDeadlineWake>().Wake(DungeonDeadlineKind.Altar);
        await Until(() => Scans(host, Altar) >= altar + 2);
        await Task.Delay(200);

        Assert.Equal(guardian, Scans(host, Guardian));
        Assert.Equal(room, Scans(host, Room));
    }

    [Fact]
    public async Task Only_the_guardian_source_rescans_on_its_idle_poll()
    {
        await using var host = new TimerSourceHost();
        await host.StartAsync(host.Hosted(nameof(DungeonDeadlineScheduler)));
        await Until(() => Scans(host, Altar) >= 2 && Scans(host, Guardian) >= 2 && Scans(host, Room) >= 2
            && host.Clock.ActiveTimers > 0);
        var (altar, guardian, room) = (Scans(host, Altar), Scans(host, Guardian), Scans(host, Room));

        host.Clock.Advance(TimeSpan.FromSeconds(30));
        await Until(() => Scans(host, Guardian) >= guardian + 2);
        await Task.Delay(200);

        Assert.Equal(altar, Scans(host, Altar));
        Assert.Equal(room, Scans(host, Room));
    }

    [Fact]
    public Task An_altar_state_wakes_its_source_on_commit_and_not_on_rollback() => TestGameplay.RunAsync(async () =>
    {
        var scope = new DomainTransactionScope();
        var wake = new RecordingWake();
        var store = new AltarStateStore(wake);
        var state = new AltarDungeonState(DungeonRunId.New(),
            new(144, AltarDungeonKind.Endless, 1, 1, 1, 0, [], new(0, 0, 0, 0, [], [], [], [], ""), 3),
            [], DateTimeOffset.UnixEpoch, 1, 12345);

        await using (scope.Begin()) store.Put(state, scope);
        Assert.Empty(wake.Kinds);

        await using (var transaction = scope.Begin())
        {
            store.Put(state, scope);
            Assert.Empty(wake.Kinds);
            await transaction.CommitAsync(default);
        }
        Assert.Equal([DungeonDeadlineKind.Altar], wake.Kinds);
    });

    [Fact]
    public Task A_guardian_runtime_wakes_its_source_on_commit_and_not_on_rollback() => TestGameplay.RunAsync(async () =>
    {
        var scope = new DomainTransactionScope();
        var wake = new RecordingWake();
        var store = new GuardianAltarRuntimeStore(scope, wake);
        var session = new SelectedSessionIdentity(new(1), new(1), new("guardian-deadline"), 1, 1, 10011);

        await using (scope.Begin()) store.Put(session.CharacterId, new(session, true, null));
        Assert.Empty(wake.Kinds);

        await using (var transaction = scope.Begin())
        {
            store.Put(session.CharacterId, new(session, true, null));
            await transaction.CommitAsync(default);
        }
        Assert.Equal([DungeonDeadlineKind.GuardianAltar], wake.Kinds);
    });

    private static int Scans(TimerSourceHost host, string label) => host.Queue.Ran.Count(x => x.CommandType == label && x.Completed);

    private static async Task Until(Func<bool> done)
    {
        using var timeout = new CancellationTokenSource(Patience);
        while (!done()) await Task.Delay(20, timeout.Token);
    }

    private sealed class IdleSource(DungeonDeadlineKind kind) : DungeonDeadlineSource
    {
        public override DungeonDeadlineKind Kind => kind;
        public override ValueTask<DateTimeOffset?> RunAsync(DungeonDeadlineRound round, CancellationToken token) =>
            ValueTask.FromResult<DateTimeOffset?>(null);
    }

    private sealed class RecordingWake : IDungeonDeadlineWake
    {
        public List<DungeonDeadlineKind> Kinds { get; } = [];
        public void Wake(DungeonDeadlineKind kind) => Kinds.Add(kind);
    }
}
