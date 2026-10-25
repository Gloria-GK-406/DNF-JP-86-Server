using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Tests.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class CommonExperienceEffectTests
{
    [Theory]
    [InlineData(30u)]
    [InlineData(0u)]
    public async Task A_clear_cannot_award_another_accounts_character(uint earned)
    {
        using var shop = new ShopFixture(s =>
        {
            s.AddSingleton<IExperienceRuleService>(new FixedProgression());
            s.AddSingleton<IExperienceCalculationDomainService>(new Calculation { Earned = earned });
        });
        var command = new GrantClearExperienceCommand(new(999), new(1), 144, new(3, 0, 1, 80, 10));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await shop.Services.GetRequiredService<ICommandBus>().SendAsync(command));
        Assert.Equal(0, shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task All_three_commands_report_the_applied_award_and_exact_execution_context()
    {
        var calculation = new Calculation();
        var trace = new EffectTrace();
        using var shop = Shop(calculation, trace);
        var bus = shop.Services.GetRequiredService<ICommandBus>();
        var clear = await bus.SendAsync(Clear(shop.Session));
        var kill = await KillDomainAsync(shop, experienceOnly: true);
        var canonical = await KillDomainAsync(shop);
        Assert.Equal(3, trace.Results.Count);
        Assert.Same(clear.Award, trace.Results[0]);
        Assert.Same(kill.Award, trace.Results[1]);
        Assert.Same(canonical.Award, trace.Results[2]);
        Assert.All(trace.Contexts, context =>
        {
            Assert.Equal(shop.Session, context.Session);
            Assert.Equal(shop.Clock.GetUtcNow(), context.At);
        });
        Assert.IsType<ClearExperienceContext>(calculation.Calls[0].Source);
        Assert.All(calculation.Calls.Skip(1), call => Assert.IsType<KillExperienceContext>(call.Source));
        Assert.Equal(90, shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_stale_or_different_recipient_cannot_write(bool otherCharacter)
    {
        using var shop = Shop(new Calculation(), new EffectTrace());
        var wrong = otherCharacter ? shop.Session with { CharacterId = new(2) }
            : shop.Session with { ConnectionGeneration = shop.Session.ConnectionGeneration + 1 };
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await shop.Services.GetRequiredService<ICommandBus>().SendAsync(Clear(wrong)));
        Assert.Equal(0, shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Quest_flush_failure_after_experience_restores_both_durable_ledgers()
    {
        var trace = new EffectTrace();
        var quests = new OrdinaryQuestEffectTests.Rules();
        quests.Quest = quests.Quest with { ObjectiveTypeCode = "hunt enemy", InitialTrigger = 1,
            KillObjectives = QuestKillObjectiveParser.Parse("hunt enemy", [144, -1, 13099, HuntEnemyTargetTypes.Monster, 1]) };
        using var shop = Shop(new Calculation(), trace, s => s.AddSingleton<IQuestContentRuleService>(quests));
        shop.Sql("INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) VALUES(1,0,2001,1); CREATE TRIGGER fail_quest BEFORE UPDATE ON character_active_quests BEGIN SELECT RAISE(ABORT,'quest flush failed'); END");
        await Assert.ThrowsAsync<SqliteException>(async () => await KillDomainAsync(shop));
        Assert.Single(trace.Results);
        Assert.Equal(0, shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(1, shop.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=1"));
        Assert.Null(shop.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
        shop.Sql("DROP TRIGGER fail_quest");
        var result = await KillDomainAsync(shop);
        Assert.Equal(30u, result.Award!.Total);
        Assert.Equal(0, shop.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=1"));
    }

    [Fact]
    public async Task Clear_late_prepare_failure_returns_no_success_and_leaves_the_next_award_usable()
    {
        var trace = new EffectTrace { Prepare = _ => throw new IOException("late prepare") };
        using var shop = Shop(new Calculation(), trace);
        await Assert.ThrowsAsync<IOException>(async () => await shop.Services.GetRequiredService<ICommandBus>().SendAsync(Clear(shop.Session)));
        Assert.Single(trace.Results);
        Assert.Equal(0, shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Null(shop.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
        trace.Prepare = null;
        Assert.Equal(30u, (await shop.Services.GetRequiredService<ICommandBus>().SendAsync(Clear(shop.Session))).Award!.Total);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Different_result_commands_wait_for_prepare_then_price_committed_or_rolled_back_progress(bool fail)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trace = new EffectTrace { Prepare = async ct =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            if (fail) throw new IOException("prepare refused");
        }};
        var calculation = new Calculation();
        using var shop = Shop(calculation, trace, RegisterQueuedKill);
        shop.Sql("UPDATE characters SET level=1,exp=90 WHERE character_id=1");
        var bus = shop.Services.GetRequiredService<ICommandBus>();
        var first = bus.SendAsync(Clear(shop.Session)).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        trace.Prepare = null;
        var second = bus.SendAsync(new QueuedKillDomainCommand(shop.Session)).AsTask();
        try
        {
            await Task.Delay(100);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Single(calculation.Calls);
            Assert.Equal(90, shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        }
        finally { release.TrySetResult(); }
        if (fail) await Assert.ThrowsAsync<IOException>(() => first);
        else Assert.Equal(120u, (await first).Award!.Total);
        var result = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(fail ? (byte)1 : (byte)2, calculation.Calls[1].Level);
        Assert.Equal(fail ? 120u : 150u, result.Award!.Total);
        Assert.IsType<ClearExperienceContext>(calculation.Calls[0].Source);
        Assert.IsType<KillExperienceContext>(calculation.Calls[1].Source);
        Assert.All(calculation.Calls, call => Assert.True(call.InTransaction));
        Assert.Equal((int)result.Award.Total, shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Null(shop.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
    }

    [Fact]
    public async Task Durable_unknown_result_blocks_the_same_recipient_before_another_payment()
    {
        var trace = new EffectTrace { Commit = _ => throw new IOException("nonfallible publication") };
        var calculation = new Calculation();
        using var shop = Shop(calculation, trace);
        var bus = shop.Services.GetRequiredService<ICommandBus>();
        await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () => await bus.SendAsync(Clear(shop.Session)));
        Assert.Equal(30, shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        trace.Commit = null;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await bus.SendAsync(Clear(shop.Session)));
        Assert.Single(calculation.Calls);
        Assert.Equal(30, shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Unknown_fence_precedes_transaction_gate_release_and_queued_effects_never_price()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitted = new AdmissionProbe();
        var calculation = new Calculation();
        var trace = new EffectTrace { Prepare = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); },
            Commit = _ => throw new IOException("nonfallible") };
        using var shop = Shop(calculation, trace, services => { RegisterQueuedKill(services); services.AddSingleton<ICommandPipelineBehavior<QueuedKillResult>>(admitted); });
        var bus = shop.Services.GetRequiredService<ICommandBus>();
        var first = bus.SendAsync(Clear(shop.Session)).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = bus.SendAsync(new QueuedKillDomainCommand(shop.Session)).AsTask();
        // The second CMD is queued behind the first on the executor, so it is not admitted before the first settles.
        Assert.False(admitted.Entered.Task.IsCompleted);
        Assert.False(second.IsCompleted);
        release.TrySetResult();
        await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.False(admitted.Entered.Task.IsCompleted);
        Assert.Single(calculation.Calls);
        Assert.Equal(30, shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        var safety = shop.Services.GetRequiredService<SelectedSessionSafety>();
        Assert.True(shop.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().StandingOf(shop.Session)?.Fenced == true);
        Assert.True(safety.IsBlocked(shop.Connection));
        Assert.False(safety.IsBlocked(shop.Connection with { }));
        Assert.Null(shop.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
    }

    [Fact]
    public async Task Publication_fence_is_synchronous_even_when_discard_waits_for_lifecycle_exclusive()
    {
        using var shop = Shop(new Calculation(), new EffectTrace());
        var safety = shop.Services.GetRequiredService<SelectedSessionSafety>();
        // The publication failure and its discard run inside one CMD; no lifecycle holder can make it wait.
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
        var recipient = safety.Capture(shop.Session);
        var failure = safety.PublicationFailedAsync(recipient, new IOException("claim lost"), "test").AsTask();
            Assert.True(shop.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().StandingOf(shop.Session)?.Fenced == true);
            Assert.True(safety.IsBlocked(shop.Connection));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await shop.Services.GetRequiredService<ICommandBus>().SendAsync(Clear(shop.Session)));
        Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure, (await failure.WaitAsync(TimeSpan.FromSeconds(5))).Reason);
        });
    }

    [Fact]
    public async Task Effect_award_settles_inside_its_clearing_cmd()
    {
        using var shop = Shop(new Calculation(), new EffectTrace());
        // The command is one job; teardown is a later job and cannot interleave with the settlement.
        var first = TestGameplay.RunAsync(shop.Services, async () =>
            await TestEffectFootprints.NonEffect(shop.Services, Dfo.Server.Data.Networking.GameMessageMetadata.Empty,
                () => shop.Services.GetRequiredService<ICommandBus>().SendAsync(Clear(shop.Session))));
        Assert.Equal(30u, (await first.WaitAsync(TimeSpan.FromSeconds(5))).Award!.Total);
        Assert.Equal(30, shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    private sealed class AdmissionProbe : ICommandPipelineBehavior<QueuedKillResult>
    {
        public int Order => 56;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<QueuedKillResult> ExecuteAsync(CommandExecutionContext context, ICommand command,
            CommandExecutionDelegate<QueuedKillResult> next, CancellationToken ct) { Entered.TrySetResult(); return next(); }
    }

    [Fact]
    public async Task Finish_quest_propagates_exact_session_into_actual_effect_context()
    {
        var trace = new EffectTrace();
        using var shop = Shop(new Calculation(), trace, services => services.AddSingleton<IQuestContentRuleService>(new OrdinaryQuestEffectTests.Rules()));
        shop.Sql("INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) VALUES(1,0,2001,0)");
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(new Dfo.Server.Protocol.Game.QuestCompletion.FinishQuestCommand(
            new(1),new(1),2001,ushort.MaxValue,1,0,shop.Session));
        Assert.True(result.Finished);
        Assert.Equal(shop.Session, Assert.Single(trace.Contexts).Session);
    }

    [Fact]
    public void All_selected_session_commands_have_their_declared_read_or_transactional_registration()
    {
        string[] readOnly = ["QueryJoustStateCommand","QueryJoustHistoryCommand","QueryTitleBookCommand"];
        string[] direct = ["ActivateClientHotkeyProfileCommand", "LeaveDungeonCommand"];
        string[] expected = ["StartBossTowerCommand","LeaveBossTowerCommand","SynchronizeBossTowerLeaderCommand","SelectBossTowerResultCommand","AltarActionCommand","AltarDeathBatchCommand","BeginTournamentRewardsCommand","SelectTournamentRewardCommand","EliminateTournamentCommand","EquipCarriedTitleReplacingMirrorCommand","ClaimWeeklyAttendanceCommand","UseEntryAllowanceCommand","IncreaseStatusCommand","UseFatiguePotionCommand","UseLevelUpTicketCommand","QuestClearCouponCommand","LearnSkillCommand","FinishQuestCommand",
            "GrantClearExperienceCommand","MarriageCeremonyCommand", "MarriageProposalCommand","CapsuleCommand","HousingCommand","CompleteWeddingCleanupCommand","EnterMarriageChurchCommand","PrepareMarriageChurchCommand","WeddingCommentCommand", "WeddingGiftCommand","WeddingLetterCommand","WeddingLovePointCommand",
            "JoustBetCommand","StoreTitleCommand","WithdrawTitleCommand","SetTitleAppearanceCommand","ReconcileAchievementCommand","SaveCharacterOptionCommand","SaveClientSettingsCommand"];
        var actual = typeof(GrantClearExperienceCommand).Assembly.GetTypes().Where(x => !x.IsAbstract && typeof(ISelectedSessionCommand).IsAssignableFrom(x)).ToArray();
        Assert.Equal(expected.Concat(readOnly).Concat(direct).Order(),actual.Select(x => x.Name).Order());
        using var shop = Shop(new Calculation(),new EffectTrace());
        var registry = shop.Services.GetRequiredService<CommandHandlerRegistry>();
        var use = registry.GetRegistration(typeof(Dfo.Server.Protocol.Game.UseStackable.GameUseStackableCommand));
        Assert.Equal(CommandRouteKind.Protocol, use.Route);
        Assert.Equal(CommandExecutionKind.Direct, use.ExecutionKind);
        var buy = registry.GetRegistration(typeof(Dfo.Server.Protocol.Game.BuyItem.GameBuyItemCommand));
        Assert.Equal(CommandRouteKind.Protocol, buy.Route);
        Assert.Equal(CommandExecutionKind.Direct, buy.ExecutionKind);
        Assert.Equal(2, buy.ProtocolMessages!.Count);
        Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(buy.CommandType));
        foreach (var type in new[] { typeof(Dfo.Server.Protocol.Game.ReinforceItem.GameReinforceItemCommand), typeof(Dfo.Server.Protocol.Game.DisjointItem.GameDisjointItemCommand), typeof(Dfo.Server.Protocol.Game.EnchantByBead.GameEnchantByBeadCommand) })
        {
            var equipment = registry.GetRegistration(type);
            Assert.Equal(CommandRouteKind.Protocol, equipment.Route);
            Assert.Equal(CommandExecutionKind.Direct, equipment.ExecutionKind);
            Assert.Single(equipment.ProtocolMessages!);
            Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, equipment.RegistrationExits);
            Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(type));
        }
        foreach (var (type, count) in new[] { (typeof(Dfo.Server.Protocol.Game.MagicSeal.GameMagicSealCommand), 2), (typeof(Dfo.Server.Protocol.Game.Forging.GameForgingCommand), 1), (typeof(Dfo.Server.Protocol.Game.Outworld.GameOutworldCommand), 2) })
        {
            var option = registry.GetRegistration(type);
            Assert.Equal(CommandRouteKind.Protocol, option.Route);
            Assert.Equal(CommandExecutionKind.Direct, option.ExecutionKind);
            Assert.Equal(count, option.ProtocolMessages!.Count);
            Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, option.RegistrationExits);
            Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(type));
        }
        Assert.All(actual, type => Assert.Equal(readOnly.Contains(type.Name)?CommandExecutionKind.ReadOnly:direct.Contains(type.Name)?CommandExecutionKind.Direct:CommandExecutionKind.Transactional,
            registry.GetRegistration(type).ExecutionKind));
        Assert.Contains(shop.Services.GetServices<ICommandPipelineBehavior<ClearExperienceResult>>(),x => x.Order == 45);
        Assert.Contains(shop.Services.GetServices<ICommandPipelineBehavior<ClearExperienceResult>>(),x => x.Order == 58);
    }

    [Fact]
    public async Task Logger_failure_does_not_replace_unknown_cause_and_the_exact_connection_is_closed_and_blocked()
    {
        var closer = new CallbackCloser();
        var trace = new EffectTrace { Commit = _ => throw new IOException("durable publication") };
        using var shop = Shop(new Calculation(),trace,services =>
        {
            closer.Use(services);
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<SelectedSessionSafety>>(new ThrowingLogger());
        });
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () => await shop.Services.GetRequiredService<ICommandBus>().SendAsync(Clear(shop.Session)));
        Assert.IsType<IOException>(error.InnerException);
        await closer.Callback.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(shop.Connection,closer.Expected);
        Assert.Equal(30,shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.True(shop.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(shop.Connection));
    }
    private sealed class ThrowingLogger : Microsoft.Extensions.Logging.ILogger<SelectedSessionSafety>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level,Microsoft.Extensions.Logging.EventId id,TState state,Exception? error,Func<TState,Exception?,string> formatter) => throw new IOException("logger");
    }
    /// <summary>
    /// The fixture's real closer (TASK-25): an exact close is registered in the outbox, and the callback runs at that
    /// registration point with the context the close was registered for. An ID-only close stays forbidden here.
    /// </summary>
    private sealed class CallbackCloser : Dfo.Server.Abstractions.Networking.INetworkConnectionCloser
    {
        private RecordedOutbound? outbound;
        public Dfo.Server.Data.Networking.NetworkConnectionContext? Expected;
        public TaskCompletionSource Callback { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private RecordedOutbound Outbound => outbound ?? throw new InvalidOperationException("The fixture has not been built.");
        public void Use(IServiceCollection services) =>
            services.AddSingleton<Dfo.Server.Abstractions.Networking.INetworkConnectionCloser>(provider =>
            {
                outbound = provider.GetRequiredService<RecordedOutbound>();
                return this;
            });
        public ValueTask RequestCloseAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,CancellationToken ct) => throw new InvalidOperationException("ID-only close is forbidden here.");
        public async ValueTask RequestCloseExactAsync(Dfo.Server.Data.Networking.NetworkConnectionContext expected,CancellationToken ct)
        {
            // The close is only requested here; teardown is a later lifecycle CMD (see the test body).
            Assert.Equal(CancellationToken.None,ct);
            await Outbound.Closer.RequestCloseExactAsync(expected, ct);
            Expected = Outbound.Entries.LastOrDefault(entry => entry.IsClose && entry.Connection.ConnectionId == expected.ConnectionId)?.Connection;
            Callback.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rollback_failure_and_nested_uncertainty_are_fenced_in_actual_bus(bool wrapped)
    {
        var cause = new IOException("prepare");
        var unknown = new TransactionOutcomeUncertainException(TransactionUncertaintyReason.RollbackFailure,"unknown",cause);
        var trace = new EffectTrace { Prepare = _ => throw (wrapped ? new AggregateException(new IOException("other"),new InvalidOperationException("wrapper",unknown)) : cause),
            Rollback = wrapped ? null : _ => throw new IOException("rollback participant") };
        var calculation = new Calculation();
        using var shop = Shop(calculation,trace);
        var error = await Assert.ThrowsAnyAsync<Exception>(async () => await shop.Services.GetRequiredService<ICommandBus>().SendAsync(Clear(shop.Session)));
        Assert.True(TransactionOutcomeUncertainException.Contains(error));
        Assert.Equal(0,shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        trace.Prepare = null; trace.Rollback = null;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await shop.Services.GetRequiredService<ICommandBus>().SendAsync(Clear(shop.Session)));
        Assert.Single(calculation.Calls);
    }

    // Test-only registration preserves the original cross-TResult bus/FIFO regression. It
    // executes the real reward domain inside the bus's real transaction and legacy footprint;
    // it neither retains a production fine command nor bypasses admission with a domain driver.
    private sealed record QueuedKillDomainCommand(SelectedSessionIdentity Session) : ICommand<QueuedKillResult>, ISelectedSessionCommand;
    private sealed record QueuedKillResult(ExperienceAward? Award);
    private sealed class QueuedKillDomainCommandHandler(IMonsterKillRewardsDomainService rewards)
        : ICommandHandler<QueuedKillDomainCommand, QueuedKillResult>
    {
        public async ValueTask<QueuedKillResult> HandleAsync(CommandExecutionContext context, QueuedKillDomainCommand command, CancellationToken ct)
        {
            var session=command.Session;
            var result=await rewards.ApplyAsync(session,session.AccountId,session.CharacterId,
                new(13099,5,MonsterRank.Ordinary,true,false),144,0,1,1,default,false,null,null,null,null,
                context.LegacyFootprint,ct);
            return new(result.Award);
        }
    }
    private static void RegisterQueuedKill(IServiceCollection services) =>
        services.AddCommandHandler<QueuedKillDomainCommand,QueuedKillResult,QueuedKillDomainCommandHandler>(CommandExecutionKind.Transactional);

    private static GrantClearExperienceCommand Clear(SelectedSessionIdentity? session) =>
        new(new(1), new(1), 144, new(3, 0, 1, 80, 10), session);
    private static Task<MonsterRewardResult> KillDomainAsync(ShopFixture shop, bool experienceOnly=false) =>
        TestGameplay.RunAsync(shop.Services, async () =>
        {
            await using var tx=shop.Services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>().Begin();
            MonsterRewardResult result;
            if(experienceOnly)
            {
                var award=await TestEffectFootprints.Run(shop.Session,cap=>shop.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterDomainService>().Get(new(1)).AddExpAsync(
                    new KillExperienceContext(new ExperienceOccasion(ExperienceEvent.MonsterKill,new(1),new(1),144),5,MonsterRank.Ordinary,0,1,1),shop.Clock.GetUtcNow(),shop.Session,cap,default));
                result=new(award,[]);
            }
            else result=await TestEffectFootprints.Run(new EffectFootprintOwner(shop.Session.AccountId,shop.Session.CharacterId,shop.Session), cap => MonsterRewardDomainDriver.ApplyAsync(shop.Services.GetRequiredService<IMonsterKillRewardsDomainService>(), new(new(1),new(1),new(13099,5,MonsterRank.Ordinary),144,0,1,Session:shop.Session), cap, default));
            await tx.CommitAsync(default);return result;
        });
    private static ShopFixture Shop(Calculation calculation, EffectTrace trace, Action<IServiceCollection>? configure = null) =>
        new(s =>
        {
            s.AddSingleton<IExperienceRuleService>(new FixedProgression());
            s.AddSingleton<IDungeonDifficultyRuleService>(new Dfo.Server.Application.Tests.Support.TestDungeonDifficultyRules());
            s.AddSingleton<IExperienceCalculationDomainService>(sp => { calculation.Transactions = sp.GetRequiredService<IDomainTransactionAccessor>(); return calculation; });
            s.AddSingleton<IApplyEffectsDomainService>(sp =>
            {
                trace.Inner = ActivatorUtilities.CreateInstance<ApplyEffectsDomainService>(sp);
                trace.Transactions = sp.GetRequiredService<IDomainTransactionAccessor>();
                return trace;
            });
            configure?.Invoke(s);
        });

    internal sealed class Calculation : IExperienceCalculationDomainService
    {
        public IDomainTransactionAccessor? Transactions { get; set; }
        public uint Earned { get; set; } = 30;
        public List<(ExperienceCalculationContext Source, byte Level, bool InTransaction)> Calls { get; } = [];
        public ValueTask<ExperienceBreakdown> CalculateAsync(ExperienceCalculationContext source, byte level, CancellationToken ct)
        {
            Calls.Add((source, level, Transactions?.Current is not null));
            return ValueTask.FromResult(new ExperienceBreakdown(Base: Earned));
        }
    }
    private sealed class FixedProgression : IExperienceRuleService
    {
        public DungeonExperienceProgressionPolicy Progression() => new([100,250,450,700], 0, 1, 100);
        public uint WorthOfKill(in KillCircumstances kill) => throw new NotSupportedException();
        public bool IsMonster(byte type) => throw new NotSupportedException();
        public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear) => throw new NotSupportedException();
        public IReadOnlyList<ExperienceModifier> ModifiersFor(ExperienceEvent occasion, IReadOnlyList<AccountPremiumEntryState> premiums, DateTimeOffset at, int channel, int dungeon) => throw new NotSupportedException();
    }
    private sealed class EffectTrace : IApplyEffectsDomainService
    {
        public IApplyEffectsDomainService Inner { get; set; } = null!;
        public IDomainTransactionAccessor Transactions { get; set; } = null!;
        public Func<CancellationToken, ValueTask>? Prepare { get; set; }
        public Func<CancellationToken, ValueTask>? Commit { get; set; }
        public Func<CancellationToken, ValueTask>? Rollback { get; set; }
        public List<ExperienceAward> Results { get; } = [];
        public List<EffectExecutionContext> Contexts { get; } = [];
        public async ValueTask<ApplyEffectsResult> ApplyAsync(EffectExecutionContext context, IReadOnlyList<DomainEffect> effects, CancellationToken ct)
        {
            var result = await Inner.ApplyAsync(context, effects, ct);
            Results.AddRange(result.Changes<CharacterExperienceChange>().Select(change => change.Award));
            Contexts.Add(context);
            if (Prepare is not null || Commit is not null || Rollback is not null) Transactions.Current!.Enlist(this, () => new LatePrepare(Prepare, Commit, Rollback));
            return result;
        }
    }
    private sealed class LatePrepare(Func<CancellationToken, ValueTask>? prepare, Func<CancellationToken, ValueTask>? commit, Func<CancellationToken, ValueTask>? rollback) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => prepare?.Invoke(ct) ?? ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => commit?.Invoke(ct) ?? ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => rollback?.Invoke(ct) ?? ValueTask.CompletedTask;
    }
}
