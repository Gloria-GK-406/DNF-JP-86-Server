using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Scheduling;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Protocol.Game.Altar;
using Dfo.Server.Protocol.Game.Fatigue;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// What a timer CMD reaches downstream runs on the executor: the sender for a periodic refresh, the
/// domain service for a wake-up loop, and at most one CMD of a source is outstanding (RQ-006).
/// </summary>
[Collection("Scheduler")]
public sealed class TimerSourceDownstreamTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private const string Fatigue = "timer:FatigueDailyRefreshService";

    [Fact]
    public async Task A_fatigue_refresh_sends_from_its_timer_cmd_whether_the_loop_or_the_entry_drives_it()
    {
        var clock = new SchedulerClock();
        using var shop = new ShopFixture(services => services.AddSingleton<TimeProvider>(clock));
        var sent = new ConcurrentQueue<(bool OnExecutor, GameplayCallChain? Chain)>();
        shop.Sender.BeforeSend = () => sent.Enqueue((GameplayJob.IsOnExecutor, GameplayJob.Current));
        var refresh = ActivatorUtilities.CreateInstance<FatigueDailyRefreshService>(shop.Services);
        await SpendAsync(shop, 20);

        await refresh.RefreshAsync(CancellationToken.None);
        Assert.Single(sent);

        await SpendAsync(shop, 5);
        await refresh.StartAsync(CancellationToken.None);
        try
        {
            await Until(() => clock.ActiveTimers > 0);
            clock.Advance(TimeSpan.FromMinutes(1));
            await Until(() => sent.Count == 2);
        }
        finally
        {
            await refresh.StopAsync(CancellationToken.None).WaitAsync(Patience);
        }

        Assert.All(sent, seen =>
        {
            Assert.True(seen.OnExecutor);
            var chain = Assert.NotNull(seen.Chain);
            Assert.Equal(GameplayCallSource.Timer, chain.Source);
            Assert.Equal(Fatigue + "/session", chain.CommandType);
            Assert.Equal(1, chain.Depth); Assert.NotNull(chain.ParentId); Assert.NotEqual(Guid.Empty, chain.RootId);
            Assert.Equal(chain.RootId, chain.ParentId);
        });
    }

    [Fact]
    public async Task A_due_tick_while_the_previous_one_waits_is_not_enqueued_beside_it()
    {
        var clock = new SchedulerClock();
        RecordingGameplayQueue? queue = null;
        using var shop = new ShopFixture(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IGameplayQueue>(sp => queue = new RecordingGameplayQueue(
                new GameplayQueue(sp.GetRequiredService<GameplayShardSelector>())) { Watched = Fatigue });
        });
        var recorder = Assert.IsType<RecordingGameplayQueue>(shop.Services.GetRequiredService<IGameplayQueue>());
        var refresh = ActivatorUtilities.CreateInstance<FatigueDailyRefreshService>(shop.Services);
        var blocker = new ExecutorBlocker();
        await blocker.HoldAsync(recorder, Patience);

        await refresh.StartAsync(CancellationToken.None);
        try
        {
            await Until(() => clock.ActiveTimers > 0);
            clock.Advance(TimeSpan.FromMinutes(1));
            await Until(() => recorder.EnqueuedCount(Fatigue) == 1);
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(100);
            Assert.Equal(1, recorder.EnqueuedCount(Fatigue));

            await blocker.ReleaseAsync(Patience);
            await Until(() => recorder.Ran.Count(x => x.CommandType == Fatigue) == 2);
            await Task.Delay(100);
        }
        finally
        {
            await refresh.StopAsync(CancellationToken.None).WaitAsync(Patience);
        }

        // The second due tick became one more CMD only after the first had completed.
        Assert.Equal(2, recorder.EnqueuedCount(Fatigue));
        Assert.Equal(1, recorder.MaximumOutstanding);
        Assert.Same(queue, recorder);
    }

    [Fact]
    public async Task An_altar_wake_up_round_reads_its_deadlines_on_the_executor()
    {
        var altars = new RecordingAltars();
        await using var host = new TimerSourceHost(services => services.AddSingleton<IAltarDungeonDomainService>(altars));
        await host.StartAsync(host.Hosted(nameof(AltarScheduler)));

        await Until(() => altars.Seen.Count >= 2);
        var firstRound = altars.Seen.Count;
        host.Services.GetRequiredService<AltarWakeSignal>().Wake();
        await Until(() => altars.Seen.Count >= firstRound + 2);

        Assert.All(altars.Seen, seen =>
        {
            Assert.True(seen.OnExecutor);
            var chain = Assert.NotNull(seen.Chain);
            Assert.Equal(GameplayCallSource.Timer, chain.Source);
            Assert.Equal("timer:AltarScheduler", chain.CommandType);
        });
    }

    private static Task SpendAsync(ShopFixture shop, ushort amount)
    {
        var fatigue = shop.Services.GetRequiredService<ICharacterFatigueDomainService>();
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        return TestGameplay.RunAsync(shop.Services, async () =>
        {
            await using var tx = scope.Begin();
            await fatigue.SpendAsync(shop.Session.CharacterId, amount, default);
            await tx.CommitAsync(default);
        });
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Patience);
        while (!condition()) await Task.Delay(1, timeout.Token);
    }

    private sealed class RecordingAltars : IAltarDungeonDomainService
    {
        public ConcurrentQueue<(bool OnExecutor, GameplayCallChain? Chain)> Seen { get; } = new();

        public IReadOnlyList<AltarDeadline> Pending()
        {
            Seen.Enqueue((GameplayJob.IsOnExecutor, GameplayJob.Current));
            return [];
        }

        public bool Supports(int dungeonId) => false;
        public AltarDungeonState? Find(DungeonRunId runId) => null;
        public ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<AltarActionResult?> ReadyAsync(SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<AltarActionResult?> AdvanceAsync(DungeonRunId runId, long roomVersion, long generation, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<AltarActionResult?> SelectDifficultyAsync(SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, long generation, byte difficulty, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<AltarActionResult?> FailAsync(DungeonRunId runId, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<AltarActionResult?> SettleBeforeExitAsync(DungeonRunId runId, CancellationToken ct, CharacterId? participant = null) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AltarQuotaSnapshot>> ReadQuotaAsync(CharacterId characterId, CancellationToken ct) => throw new NotSupportedException();
        public bool CanRevive(CharacterId characterId, DungeonRunId runId) => false;
        public bool StageRevival(CharacterId characterId, DungeonRunId runId) => false;
    }
}
