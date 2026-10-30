using Dfo.Server.Abstractions.Inventory;
using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Quests;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class CardSettlementLifecycleTests
{
    internal static bool Whole => typeof(SetPlayResultHandler).Assembly.GetType("Dfo.Server.Protocol.Game.DungeonClear.GameSetPlayResultCommand") is not null;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Task48_clear_scope_after_sql_commit_records_claim_assets_and_real_wire_retry(bool scopeFailure)
    {
        var calculation = new CommonExperienceEffectTests.Calculation();
        await using var probe = new EntryFailureProbe(false, scopeFailure, ordinary: true,
            commandOverride: Whole ? "GameSetPlayResultCommand" : "GrantClearExperienceCommand");
        using var f = new ShopFixture(s =>
        {
            probe.Configure(s);
            s.AddSingleton<ISelectedCharacterCurrentViewDomainService, EntryFailureProbe.CompleteView>();
            s.AddSingleton<IExperienceRuleService>(new OrdinaryQuestEffectTests.FixedProgression());
            s.AddSingleton<IDungeonDifficultyRuleService>(new Support.TestDungeonDifficultyRules());
            s.AddSingleton<IExperienceCalculationDomainService>(p =>
            {
                calculation.Transactions = p.GetRequiredService<IDomainTransactionAccessor>();
                return calculation;
            });
        });
        var runId = CharacterDeathProtocolLifecycleTests.Enter(f);
        TestGameplay.Run(f.Services, () =>
        {
            var runs = f.Services.DungeonRuns();
            var run = runs.Find(f.Session.CharacterId)!;
            Assert.NotNull(runs.TryOpenClear(f.Session.CharacterId, runId, run.RoomVersion));
        });
        var scheduledAtCleanup = false;
        probe.BeforeDispose = () => scheduledAtCleanup = Pending(f) > 0;
        var physical = probe.Sender.Add(f.Connection);
        var error = await Record.ExceptionAsync(() => probe.Wire(f, 0x2e, new byte[53]).AsTask());
        if (!scopeFailure) Assert.Null(error);
        else if (Whole) Assert.Same(probe.Cause, Assert.IsType<TransactionOutcomeUncertainException>(error).InnerException);
        else Assert.Same(probe.Cause, error);
        Assert.Equal(30, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        if (scopeFailure) Assert.Equal(Whole, scheduledAtCleanup);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(probe.Transactions).Completion);
        Assert.Single(calculation.Calls);
        Assert.True(calculation.Calls[0].InTransaction);
        if (Whole)
        {
            var declaration = Assert.Single(probe.Declarations);
            Assert.Equal(CommandWorkUnitKind.Ordinary, declaration.Kind);
            Assert.Equal(CommandFootprintMode.NonRequest, declaration.Footprint);
            var actor = Assert.Single(declaration.Participants);
            Assert.Equal(f.Session, actor.Session); Assert.Same(f.Connection, actor.Connection);
            Assert.Equal((ushort)0x2e, actor.Metadata!.Type);
        }
        TestGameplay.Run(f.Services, () =>
        {
            var runs = f.Services.DungeonRuns();
            var run = runs.Find(f.Session.CharacterId)!;
            Assert.Equal(!scopeFailure || Whole, run.IsSettled);
            Assert.NotNull(run.ClearRewardCards);
            var claim = runs.TryClaimSettlement(f.Session.CharacterId, runId);
            if (scopeFailure && !Whole) { Assert.NotNull(claim); runs.ReleaseSettlement(claim!); }
            else Assert.Null(claim);
        });
        await probe.Sender.Settled();
        Assert.Equal(!scopeFailure || Whole ? 1 : 0, physical.Sends.Count);
        if (scopeFailure) await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var before = f.Scalar("SELECT exp FROM characters WHERE character_id=1");
        var retry = await Record.ExceptionAsync(() => probe.Wire(f, 0x2e, new byte[53]).AsTask());
        if (scopeFailure && !Whole) Assert.NotNull(retry); else Assert.Null(retry);
        Assert.Equal(scopeFailure && !Whole ? before + 30 : before,
            f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(scopeFailure && !Whole ? 2 : 1, calculation.Calls.Count);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Task48_actual_timer_layout_origin_encoder_scope_and_stage_handoff(int fault)
    {
        var delay = new Delay();
        await using var probe = new EntryFailureProbe(false, Whole && fault == 2, ordinary: true,
            commandOverride: Whole ? "DungeonCardLayoutCommand" : "GrantClearExperienceCommand");
        using var f = new ShopFixture(services => { probe.Configure(services); services.AddSingleton<IDungeonCardPresentationDelay>(delay); });
        var disposed = 0;
        probe.BeforeDispose = () => disposed++;
        var run = Ready(f);
        var physical = probe.Sender.Add(f.Connection);
        GameplayCallChain publisher = default;
        var producer = f.Services.GetRequiredService<DungeonCardPresentationCoordinator>();
        var encoded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (fault == 1) f.Sender.Outbound.Encoder.Fail((_, message) =>
        {
            if (message.GetType().Name != "CardSelectRightStateResponse") return null;
            encoded.TrySetResult(); return probe.Cause;
        });
        await TestGameplay.RunAsync(f.Services, () =>
        {
            publisher = GameplayJob.Current!.Value;
            producer.ScheduleAfterSettlement(f.Session, run);
            return Task.CompletedTask;
        });
        await delay.WaitFor(TimeSpan.FromSeconds(2));
        delay.Release(TimeSpan.FromSeconds(2));
        if (fault != 1) await delay.WaitFor(TimeSpan.FromSeconds(4));
        else await encoded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await producer.DrainAsync(default);
        await probe.Sender.Settled();
        Assert.Equal(fault == 1 ? 0 : 1, physical.Sends.Count);
        Assert.Equal(0, physical.CloseCalls);
        Assert.Empty(probe.Transactions);
        if (Whole)
        {
            var context = Assert.Single(probe.Executions);
            var origin = Assert.IsType<CommandRequestContext.TrustedInternal>(context.Request).Origin;
            Assert.Equal(GameplayCallSource.Timer, origin.Kind);
            Assert.Equal(GameplayCallSource.Timer, probe.Chain.Source);
            Assert.Null(origin.SourceConnection); Assert.Null(probe.Chain.SourceConnection);
            Assert.NotEqual(publisher.RootId, probe.Chain.RootId);
            Assert.Equal(InternalCommandCancellation.BodyOnly, origin.Cancellation);
            Assert.True(context.Request!.IsClosed);
            if (fault is 1 or 2) Assert.Same(probe.Cause, context.Request.Failure);
            else Assert.Null(context.Request.Failure);
            Assert.Equal(fault == 2 ? 1 : 0, disposed);
        }
        if (fault != 1) Assert.Equal(new ushort[] { 0x45, 0x46 }, CharacterDeathOracle.Types(physical.Sends.Single()));
        TestGameplay.Run(f.Services, () => Assert.True(f.Services.DungeonRuns().Find(f.Session.CharacterId)!.CardLayoutOpen));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Task48_actual_wire_free_normalizes_index_and_paid_preserves_cost_commit_and_repeat(bool paid)
    {
        await using var probe = new EntryFailureProbe(false, false, ordinary: true,
            commandOverride: Whole ? "GameSelectCardCommand" : "SelectCardCommand");
        using var f = new ShopFixture(services => { probe.Configure(services); services.AddSingleton<ISelectedCharacterCurrentViewDomainService, EntryFailureProbe.CompleteView>(); });
        var runId = Ready(f, open: true);
        f.Sql("UPDATE character_stats SET carried_gold=100 WHERE character_id=1");
        var physical = probe.Sender.Add(f.Connection); var before = f.Gold;
        var body = new byte[] { paid ? (byte)1 : (byte)0, paid ? (byte)0 : (byte)3 };
        await probe.Wire(f, 0x47, body);
        await probe.Sender.Settled();
        Assert.Equal(before + (paid ? 11 - 13 : 7), f.Gold);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(probe.Transactions).Completion);
        if (Whole)
        {
            var declaration = Assert.Single(probe.Declarations);
            Assert.Empty(declaration.Participants); Assert.Equal(CommandFootprintMode.None, declaration.Footprint);
        }
        TestGameplay.Run(f.Services, () =>
        {
            var state = f.Services.DungeonRuns().Find(f.Session.CharacterId)!;
            var selection = Assert.Single(state.CardSelections);
            Assert.Equal(0, selection.Index); Assert.Equal(runId, state.RunId);
        });
        Assert.Single(physical.Sends);
        var expected = new ushort[] { 0x47, 0x0E, 0x036F };
        Assert.Equal(expected, CharacterDeathOracle.Types(physical.Sends.Single()));
        await probe.Wire(f, 0x47, body); await probe.Sender.Settled();
        Assert.Equal(before + (paid ? 11 - 13 : 7), f.Gold); Assert.Single(physical.Sends);
    }

    [Fact]
    public async Task Task48_real_reservation_has_no_between_attempt_progress_on_one_world_job()
    {
        await using var probe = new EntryFailureProbe(false, false, ordinary: true,
            commandOverride: Whole ? "GameSelectCardCommand" : "SelectCardCommand");
        using var f = new ShopFixture(probe.Configure); var run = Ready(f, open: true);
        var queue = f.Services.GetRequiredService<IGameplayQueue>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attemptsMayRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DungeonClearRewardCardClaim claim = null!;
        var released = false;
        var first = queue.RunAsync(new(GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.Timer),
            "test:reservation-three-attempts", async ct =>
        {
            var runs = f.Services.DungeonRuns();
            claim = Assert.IsType<DungeonClearRewardCardClaim>(runs.TryReserveClearRewardCard(f.Session.CharacterId, run, ClearRewardCardKind.Free, 0));
            entered.TrySetResult();
            await attemptsMayRun.Task.WaitAsync(ct);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                Assert.False(released);
                Assert.Null(runs.TryReserveClearRewardCard(f.Session.CharacterId, run, ClearRewardCardKind.Free, 0));
                await Task.Yield();
                Assert.False(runs.Find(f.Session.CharacterId)!.ClearRewardCards!.IsClaimed(ClearRewardCardKind.Free, 0));
            }
        }), default).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var release = queue.RunAsync(new(GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.Timer),
            "test:queued-reservation-release", _ =>
        {
            Assert.True(first.IsCompleted);
            Assert.True(f.Services.DungeonRuns().ReleaseClearRewardCard(claim));
            released = true; return ValueTask.CompletedTask;
        }), default).AsTask();
        Assert.False(release.IsCompleted);
        attemptsMayRun.TrySetResult();
        await first; await release;
        await queue.RunAsync(new(GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.Timer),
            "test:next-job-can-reserve", _ =>
        {
            Assert.True(released);
            var runs = f.Services.DungeonRuns();
            var retry = Assert.IsType<DungeonClearRewardCardClaim>(runs.TryReserveClearRewardCard(f.Session.CharacterId, run, ClearRewardCardKind.Free, 0));
            Assert.True(runs.ReleaseClearRewardCard(retry)); return ValueTask.CompletedTask;
        }), default);

    }


    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Task48_manual_free_cancels_before_projection_paid_and_refusal_do_not(int mode)
    {
        var delay = new Delay();
        await using var probe = new EntryFailureProbe(false, false, ordinary: true,
            commandOverride: Whole ? "GameSelectCardCommand" : "SelectCardCommand");
        using var f = new ShopFixture(services =>
        {
            probe.Configure(services); services.AddSingleton<IDungeonCardPresentationDelay>(delay);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, EntryFailureProbe.CompleteView>();
        });
        var run = Ready(f); var producer = f.Services.GetRequiredService<DungeonCardPresentationCoordinator>();
        var physical = probe.Sender.Add(f.Connection);
        TestGameplay.Run(f.Services, () => producer.ScheduleAfterSettlement(f.Session, run));
        await delay.WaitFor(TimeSpan.FromSeconds(2)); delay.Release(TimeSpan.FromSeconds(2));
        await delay.WaitFor(TimeSpan.FromSeconds(4)); await probe.Sender.Settled();
        Assert.Single(physical.Sends);
        f.Sql($"UPDATE character_stats SET carried_gold={(mode == 2 ? 0 : 100)} WHERE character_id=1");
        var observed = false;
        f.Sender.Outbound.Encoder.Fail((_, message) =>
        {
            if (message.GetType().Name != "SelectCardResponse") return null;
            observed = true;
            Assert.Equal(mode is 0 or 3, delay.Tokens[TimeSpan.FromSeconds(4)].IsCancellationRequested);
            return mode == 3 ? probe.Cause : null;
        });
        var error = await Record.ExceptionAsync(() => probe.Wire(f, 0x47, [mode is 1 or 2 ? (byte)1 : (byte)0, 0]).AsTask());
        if (mode != 3) Assert.Null(error);
        else if (Whole) Assert.Same(probe.Cause, Assert.IsType<TransactionOutcomeUncertainException>(error).InnerException);
        else Assert.Same(probe.Cause, error);
        Assert.Equal(mode != 2, observed);
        Assert.Equal(mode is 0 or 3, delay.Tokens[TimeSpan.FromSeconds(4)].IsCancellationRequested);
        Assert.Equal(mode == 2 ? 0 : mode == 1 ? 98 : 107, f.Gold);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(probe.Transactions).Completion);
        await probe.Sender.Settled();
        Assert.Equal(mode is 0 or 1 ? 2 : 1, physical.Sends.Count);
        await producer.DrainAsync(default);
    }

    [Theory]
    [InlineData(0x45)] [InlineData(0x46)] [InlineData(0x75)]
    public async Task Task48_real_scroll_right_and_boss_routes_preserve_no_transaction_and_one_layout(int type)
    {
        await using var probe = new EntryFailureProbe(false, false, ordinary: true,
            commandOverride: Whole ? type == 0x75 ? "GameBossDieCheckCommand" : type == 0x45 ? "GameScoreScrollStateCommand" : "GameCardSelectRightStateCommand" : "GrantClearExperienceCommand");
        using var f = new ShopFixture(probe.Configure); var run = Ready(f);
        var physical = probe.Sender.Add(f.Connection);
        await probe.Wire(f, (ushort)type, type == 0x75 ? new byte[4] : []);
        await probe.Sender.Settled();
        Assert.Empty(probe.Transactions);
        if (type == 0x75) Assert.Empty(physical.Sends);
        else Assert.Equal(new ushort[] { 0x45, 0x46 }, CharacterDeathOracle.Types(Assert.Single(physical.Sends)));
        await probe.Wire(f, (ushort)type, type == 0x75 ? new byte[4] : []);
        await probe.Sender.Settled();
        Assert.Equal(type == 0x75 ? 0 : 1, physical.Sends.Count);
        TestGameplay.Run(f.Services, () => Assert.Equal(type != 0x75,
            f.Services.DungeonRuns().Find(f.Session.CharacterId)!.CardLayoutOpen));
        await f.Services.GetRequiredService<DungeonCardPresentationCoordinator>().DrainAsync(default);
    }


    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Task48_real_queued_timer_is_cancelled_in_fifo_order_and_drain_waits(bool stop)
    {
        var delay = new Delay(); var observed = new QueueLog();
        await using var probe = new EntryFailureProbe(false, false, ordinary: true,
            commandOverride: Whole ? "DungeonCardLayoutCommand" : "GrantClearExperienceCommand");
        using var f = new ShopFixture(services =>
        {
            probe.Configure(services); services.AddSingleton<IDungeonCardPresentationDelay>(delay);
            services.AddSingleton<IGameplayQueue>(p => observed.Bind(new GameplayQueue(p.GetRequiredService<GameplayShardSelector>())));
        });
        var run = Ready(f); var producer = f.Services.GetRequiredService<DungeonCardPresentationCoordinator>();
        var physical = probe.Sender.Add(f.Connection); var queue = f.Services.GetRequiredService<IGameplayQueue>();
        TestGameplay.Run(f.Services, () => producer.ScheduleAfterSettlement(f.Session, run));
        await delay.WaitFor(TimeSpan.FromSeconds(2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = queue.RunAsync(new(GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.Timer), "test:card-blocker", async ct =>
        { entered.TrySetResult(); await release.Task.WaitAsync(ct); }), default).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var cancellation = stop ? Task.CompletedTask : queue.RunAsync(new(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Timer), "test:card-cancel-before-timer", _ =>
            { producer.Cancel(f.Session.CharacterId, run); return ValueTask.CompletedTask; }), default).AsTask();
        delay.Release(TimeSpan.FromSeconds(2));
        await observed.TimerQueued.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var drain = stop ? producer.DrainAsync(default).AsTask() : null;
        if (drain is not null) Assert.False(drain.IsCompleted);
        Assert.Empty(physical.Sends);
        release.TrySetResult(); await blocker; await cancellation;
        if (drain is not null) await drain.WaitAsync(TimeSpan.FromSeconds(10));
        else await producer.DrainAsync(default);
        await probe.Sender.Settled(); Assert.Empty(physical.Sends);
        Assert.Equal(0, physical.CloseCalls); Assert.Empty(probe.Transactions);
        TestGameplay.Run(f.Services, () => Assert.False(f.Services.DungeonRuns().Find(f.Session.CharacterId)!.CardLayoutOpen));
        Assert.Equal(stop ? new[] { "test:card-blocker", "timer:DungeonCardPresentationCoordinator/layout" }
            : new[] { "test:card-blocker", "test:card-cancel-before-timer", "timer:DungeonCardPresentationCoordinator/layout" }, observed.Names);
        Assert.False(observed.TimerCancellation.CanBeCanceled); Assert.Null(observed.TimerSource);
        if (Whole) { var execution = Assert.Single(probe.Executions); Assert.IsType<CommandRequestContext.TrustedInternal>(execution.Request); Assert.True(execution.Request!.IsClosed); }
    }
    private sealed class QueueLog : IGameplayQueue
    {
        private IGameplayQueue inner = null!;
        internal readonly ConcurrentQueue<string> Names = new();
        internal readonly TaskCompletionSource TimerQueued = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken TimerCancellation;
        internal NetworkConnectionContext? TimerSource;
        internal QueueLog Bind(IGameplayQueue queue) { inner = queue; return this; }
        private void Record(string name,CancellationToken ct,NetworkConnectionContext? source)
        {
            Names.Enqueue(name);
            if (!name.StartsWith("timer:DungeonCardPresentationCoordinator/", StringComparison.Ordinal)) return;
            TimerCancellation = ct; TimerSource = source; TimerQueued.TrySetResult();
        }
        public ValueTask<T> RunAsync<T>(GameplayWork<T> work,CancellationToken ct)
        { var result = inner.RunAsync(work,ct); Record(work.CommandType,ct,work.Source);return result; }
        public ValueTask RunAsync(GameplayWork work,CancellationToken ct)
        { var result = inner.RunAsync(work,ct);Record(work.CommandType,ct,work.Source);return result; }
    }


    [Fact]
    public async Task Task48_clear_encoder_failure_keeps_claim_and_assets_but_never_schedules_layout()
    {
        var calculation = new CommonExperienceEffectTests.Calculation();
        await using var probe = new EntryFailureProbe(false, false, ordinary: true,
            commandOverride: Whole ? "GameSetPlayResultCommand" : "GrantClearExperienceCommand");
        using var f = new ShopFixture(services =>
        {
            probe.Configure(services); services.AddSingleton<ISelectedCharacterCurrentViewDomainService, EntryFailureProbe.CompleteView>();
            services.AddSingleton<IExperienceRuleService>(new OrdinaryQuestEffectTests.FixedProgression());
            services.AddSingleton<IDungeonDifficultyRuleService>(new Support.TestDungeonDifficultyRules());
            services.AddSingleton<IExperienceCalculationDomainService>(p => { calculation.Transactions = p.GetRequiredService<IDomainTransactionAccessor>();return calculation; });
        });
        var runId = CharacterDeathProtocolLifecycleTests.Enter(f);
        TestGameplay.Run(f.Services, () =>
        {
            var runs = f.Services.DungeonRuns();
            Assert.NotNull(runs.TryOpenClear(f.Session.CharacterId,runId,runs.Find(f.Session.CharacterId)!.RoomVersion));
        });
        var physical = probe.Sender.Add(f.Connection);
        f.Sender.Outbound.Encoder.Fail((_, message) => message.GetType().Name == "ExperienceNotification" ? probe.Cause : null);
        var error = await Record.ExceptionAsync(() => probe.Wire(f,0x2e,new byte[53]).AsTask());
        if (Whole) Assert.Same(probe.Cause,Assert.IsType<TransactionOutcomeUncertainException>(error).InnerException);
        else Assert.Same(probe.Cause,error);
        Assert.Equal(30,f.Scalar("SELECT exp FROM characters WHERE character_id=1"));Assert.Single(calculation.Calls);
        Assert.Equal(0,Pending(f));
        TestGameplay.Run(f.Services, () =>
        {
            var runs=f.Services.DungeonRuns();
            Assert.True(runs.Find(f.Session.CharacterId)!.IsSettled);
            Assert.Null(runs.TryClaimSettlement(f.Session.CharacterId,runId));
            Assert.Equal(Whole,f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
        });
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await probe.Sender.Settled();Assert.Empty(physical.Sends);
    }

    [Fact]
    public async Task Task48_actual_second_reward_sql_failure_rolls_back_gold_items_and_claim_then_retry_pays_once()
    {
        await using var probe = new EntryFailureProbe(false, false, ordinary: true,
            commandOverride: Whole ? "GameSelectCardCommand" : "SelectCardCommand");
        using var f = new ShopFixture(services => { probe.Configure(services); services.AddSingleton<ISelectedCharacterCurrentViewDomainService, EntryFailureProbe.CompleteView>(); });
        var runId = Ready(f, open: true, items: true);
        f.Sql("UPDATE character_stats SET carried_gold=100 WHERE character_id=1; CREATE TRIGGER fail_second_card BEFORE INSERT ON items WHEN (SELECT count(*) FROM items)>=1 BEGIN SELECT RAISE(ABORT,'second card'); END");
        var firstPhysical = probe.Sender.Add(f.Connection);
        var error = await Record.ExceptionAsync(() => probe.Wire(f,0x47,[0,0]).AsTask());
        Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(error);
        Assert.Equal(100,f.Gold); Assert.Equal(0,f.Scalar("SELECT count(*) FROM items"));
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,Assert.Single(probe.Transactions).Completion);
        TestGameplay.Run(f.Services, () => Assert.False(f.Services.DungeonRuns().Find(f.Session.CharacterId)!.ClearRewardCards!.IsClaimed(ClearRewardCardKind.Free,0)));
        await firstPhysical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await probe.Sender.Settled();Assert.Empty(firstPhysical.Sends);
        f.Sql("DROP TRIGGER fail_second_card");
        var replacement=f.Connection with{};
        probe.Replacements[replacement.ConnectionId]=replacement;
        f.Sender.Outbound.Connections.Remove(replacement.ConnectionId);f.Sender.Outbound.Connections.Add(replacement);
        var physical=probe.Sender.Add(replacement);
        await WireOn(f,replacement,0x47,[0,0]);await probe.Sender.Settled();
        Assert.Equal(107,f.Gold);Assert.Equal(1,f.Count(2000));Assert.Equal(1,f.Count(4000));
        Assert.Equal(DomainTransactionCompletion.Committed,probe.Transactions[1].Completion);
        Assert.Equal(new ushort[]{0x47,0x0E,0x0E,0x0E,0x036F},CharacterDeathOracle.Types(Assert.Single(physical.Sends)));
        await WireOn(f,replacement,0x47,[0,0]);await probe.Sender.Settled();
        Assert.Equal(107,f.Gold);Assert.Equal(1,f.Count(2000));Assert.Equal(1,f.Count(4000));Assert.Single(physical.Sends);
        TestGameplay.Run(f.Services,()=>Assert.Null(f.Services.DungeonRuns().TryReserveClearRewardCard(f.Session.CharacterId,runId,ClearRewardCardKind.Free,0)));
    }

    [Fact]
    public async Task Task48_paid_wire_prepare_failure_restores_fee_reward_and_claim_then_retry_pays_once()
    {
        var fault=new PaidPrepareFault();
        await using var probe=new EntryFailureProbe(false,false,ordinary:true,commandOverride:Whole?"GameSelectCardCommand":"SelectCardCommand");
        using var f=new ShopFixture(services=>
        {
            probe.Configure(services);services.AddSingleton<ISelectedCharacterCurrentViewDomainService,EntryFailureProbe.CompleteView>();
            var descriptor=services.Last(x=>x.ServiceType==typeof(IItemDeliveryDomainService));services.Remove(descriptor);
            services.AddSingleton<IItemDeliveryDomainService>(p=>new DeliveryWithPrepare(
                (IItemDeliveryDomainService)(descriptor.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,descriptor.ImplementationType!)),
                p.GetRequiredService<IDomainTransactionAccessor>(),p.GetRequiredService<ICharacterDomainService>(),fault));
        });
        var run=Ready(f,open:true,items:true);f.Sql("UPDATE character_stats SET carried_gold=100 WHERE character_id=1");
        fault.AtPrepare=()=>
        {
            Assert.Equal(98,fault.StagedGold);Assert.True(fault.InTransaction);Assert.Equal(1,fault.StagedItems);
            Assert.Equal(100,f.Gold); // The real SQLite write is still private to this transaction.
            return Task.CompletedTask;
        };
        var first=probe.Sender.Add(f.Connection);
        Assert.Same(fault.Cause,await Record.ExceptionAsync(()=>probe.Wire(f,0x47,[1,0]).AsTask()));
        Assert.Equal(1,fault.Prepares);Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,Assert.Single(probe.Transactions).Completion);
        Assert.Equal(100,f.Gold);Assert.Equal(0,f.Count(2000));
        TestGameplay.Run(f.Services,()=>Assert.False(f.Services.DungeonRuns().Find(f.Session.CharacterId)!.ClearRewardCards!.IsClaimed(ClearRewardCardKind.Paid,0)));
        await first.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await probe.Sender.Settled();Assert.Empty(first.Sends);
        fault.Fail=false;fault.AtPrepare=null;
        var replacement=f.Connection with{};probe.Replacements[replacement.ConnectionId]=replacement;
        f.Sender.Outbound.Connections.Remove(replacement.ConnectionId);f.Sender.Outbound.Connections.Add(replacement);
        var physical=probe.Sender.Add(replacement);
        await WireOn(f,replacement,0x47,[1,0]);await probe.Sender.Settled();
        Assert.Equal(98,f.Gold);Assert.Equal(1,f.Count(2000));Assert.Equal(DomainTransactionCompletion.Committed,probe.Transactions[1].Completion);
        Assert.Equal(new ushort[]{0x47,0x0E,0x0E,0x036F},CharacterDeathOracle.Types(Assert.Single(physical.Sends)));
        await WireOn(f,replacement,0x47,[1,0]);await probe.Sender.Settled();
        Assert.Equal(98,f.Gold);Assert.Equal(1,f.Count(2000));Assert.Single(physical.Sends);
        TestGameplay.Run(f.Services,()=>Assert.Null(f.Services.DungeonRuns().TryReserveClearRewardCard(f.Session.CharacterId,run,ClearRewardCardKind.Paid,0)));
    }
    private sealed class PaidPrepareFault
    { internal readonly IOException Cause=new("TASK48 paid award prepare");internal bool Fail=true;internal int Prepares;internal Func<Task>? AtPrepare;internal int? StagedGold;internal bool InTransaction;internal int StagedItems; }
    private sealed class DeliveryWithPrepare(IItemDeliveryDomainService actual,IDomainTransactionAccessor transactions,ICharacterDomainService characters,PaidPrepareFault fault):IItemDeliveryDomainService
    {
        public ValueTask<long> PendingQuantityAsync(CharacterId character,int item,CancellationToken ct)=>actual.PendingQuantityAsync(character,item,ct);
        public async ValueTask<ItemDeliveryReceipt> DeliverAsync(Dfo.Server.Data.Accounts.AccountId account,CharacterId character,
            IReadOnlyList<ItemDeliveryGrant> grants,string source,CancellationToken ct,ItemDeliveryMode mode,ItemDeliveryOptions? options=null)
        {
            var result=await actual.DeliverAsync(account,character,grants,source,ct,mode,options);
            fault.StagedGold=await characters.Get(character).GoldAsync(ct);fault.StagedItems=result.Inventory.Count;fault.InTransaction=transactions.Current is not null;
            transactions.Current!.Enlist(fault,()=>new Prepare(fault));return result;
        }
        private sealed class Prepare(PaidPrepareFault fault):ITransactionParticipant
        {
            public bool CommitCanFail=>false;
            public async ValueTask PrepareAsync(CancellationToken ct)
            {fault.Prepares++;if(fault.AtPrepare is not null)await fault.AtPrepare();if(fault.Fail)throw fault.Cause;}
            public ValueTask CommitAsync(CancellationToken ct)=>ValueTask.CompletedTask;
            public ValueTask RollbackAsync(CancellationToken ct)=>ValueTask.CompletedTask;
        }
    }

    private static ValueTask WireOn(ShopFixture f,NetworkConnectionContext connection,ushort type,byte[] body)
    {
        var metadata=new GameMessageMetadata(1,type,0,8){InboundOrdinal=8,SequenceIsFresh=true}.WithRawBody(body);
        var registration=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1&&x.Type==type);
        var input=((IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType)).Decode(metadata,body)!;
        return f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(connection),input,default);
    }


    [Fact]
    public async Task Task48_real_auto_flip_runs_trusted_internal_ordinary_write_sql_once()
    {
        var delay=new Delay();
        await using var probe=new EntryFailureProbe(false,false,ordinary:true,
            commandOverride:Whole?"DungeonCardAutoFlipCommand":"SelectCardCommand");
        using var f=new ShopFixture(services=>
        {probe.Configure(services);services.AddSingleton<IDungeonCardPresentationDelay>(delay);services.AddSingleton<ISelectedCharacterCurrentViewDomainService,EntryFailureProbe.CompleteView>();});
        var run=Ready(f);f.Sql("UPDATE character_stats SET carried_gold=100 WHERE character_id=1");
        var physical=probe.Sender.Add(f.Connection);var producer=f.Services.GetRequiredService<DungeonCardPresentationCoordinator>();
        GameplayCallChain publisher=default;
        TestGameplay.Run(f.Services,()=>{publisher=GameplayJob.Current!.Value;producer.ScheduleAfterSettlement(f.Session,run);});
        await delay.WaitFor(TimeSpan.FromSeconds(2));delay.Release(TimeSpan.FromSeconds(2));
        await delay.WaitFor(TimeSpan.FromSeconds(4));delay.Release(TimeSpan.FromSeconds(4));
        var deadline=DateTime.UtcNow.AddSeconds(10);
        while(f.Gold!=107 || Pending(f)!=0 || Inflight(f)!=0){if(DateTime.UtcNow>deadline)throw new TimeoutException("Actual automatic award did not finish");await Task.Delay(5);}
        await producer.DrainAsync(default);await probe.Sender.Settled();
        Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(probe.Transactions).Completion);
        if(Whole)
        {
            var execution=Assert.Single(probe.Executions);
            var origin=Assert.IsType<CommandRequestContext.TrustedInternal>(execution.Request).Origin;
            Assert.True(execution.Request!.IsClosed);Assert.Null(execution.Request.Failure);
            Assert.Equal(GameplayCallSource.Timer,origin.Kind);Assert.Null(origin.SourceConnection);Assert.Null(probe.Chain.SourceConnection);
            Assert.NotEqual(publisher.RootId,probe.Chain.RootId);
            var declaration=Assert.Single(probe.Declarations);
            Assert.Equal(CommandWorkUnitKind.Ordinary,declaration.Kind);Assert.Empty(declaration.Participants);Assert.Equal(CommandFootprintMode.None,declaration.Footprint);
        }
        Assert.Equal(2,physical.Sends.Count);
        Assert.Equal(new ushort[]{0x45,0x46},CharacterDeathOracle.Types(physical.Sends.ElementAt(0)));
        Assert.Equal(new ushort[]{0x47,0x0E,0x036F},CharacterDeathOracle.Types(physical.Sends.ElementAt(1)));
        TestGameplay.Run(f.Services,()=>Assert.Null(f.Services.DungeonRuns().TryReserveClearRewardCard(f.Session.CharacterId,run,ClearRewardCardKind.Free,0)));
        await probe.Wire(f,0x47,[0,0]);await probe.Sender.Settled();
        Assert.Equal(107,f.Gold);Assert.Equal(2,physical.Sends.Count);Assert.Equal(0,physical.CloseCalls);
    }

    private static int Inflight(ShopFixture f) => (int)typeof(DungeonCardPresentationCoordinator)
        .GetField("inflight",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
        .GetValue(f.Services.GetRequiredService<DungeonCardPresentationCoordinator>())!;
    private static int Pending(ShopFixture f) => ((System.Collections.IDictionary)typeof(DungeonCardPresentationCoordinator)
        .GetField("pending",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
        .GetValue(f.Services.GetRequiredService<DungeonCardPresentationCoordinator>())!).Count;

    private static Dfo.Server.Abstractions.World.DungeonRunId Ready(ShopFixture f, bool open = false, bool items = false)
    {
        var runId = CharacterDeathProtocolLifecycleTests.Enter(f);
        TestGameplay.Run(f.Services, () =>
        {
            var runs = f.Services.DungeonRuns();
            var run = runs.Find(f.Session.CharacterId)!;
            Assert.NotNull(runs.TryOpenClear(f.Session.CharacterId, runId, run.RoomVersion));
            Assert.NotNull(runs.FixClearRewardCards(f.Session.CharacterId, runId,
                new(ClearRewardCardKind.Free, [new(0, items ? new ClearRewardCardItem(2000,1,DropRarity.Common,0) : null)], 7),
                new(ClearRewardCardKind.Paid, [new(0, items ? new ClearRewardCardItem(2000,1,DropRarity.Common,0) : null)], 11), 13,
                items ? new(ClearRewardCardKind.BlackDiamond,[new(0,new ClearRewardCardItem(4000,1,DropRarity.Common,0))],0) : null));
            Assert.NotNull(runs.CommitSettlement(Assert.IsType<DungeonSettlementClaim>(runs.TryClaimSettlement(f.Session.CharacterId, runId))));
            if (open) Assert.NotNull(runs.TryOpenCardLayout(f.Session.CharacterId, runId));
        });
        return runId;
    }
    private sealed class Delay : IDungeonCardPresentationDelay
    {
        private readonly ConcurrentDictionary<TimeSpan,TaskCompletionSource> waits = new();
        internal readonly ConcurrentDictionary<TimeSpan,CancellationToken> Tokens = new();
        public async ValueTask WaitAsync(TimeSpan span, CancellationToken ct)
        {
            Tokens[span] = ct;
            await waits.GetOrAdd(span, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(ct);
        }
        internal async Task WaitFor(TimeSpan span)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!waits.ContainsKey(span)) { if(DateTime.UtcNow > until)throw new TimeoutException("Card stage was not scheduled");await Task.Delay(5); }
        }
        internal void Release(TimeSpan span)=>waits[span].TrySetResult();
    }
}
