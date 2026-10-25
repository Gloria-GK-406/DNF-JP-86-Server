using System.Buffers.Binary;
using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.RecoverStamina;
using Dfo.Server.Protocol.Game.ReviveCoin;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class RecoveryAndReviveProtocolCommandFlowTests(ITestOutputHelper output)
{
    private static bool Legacy => typeof(RecoverStaminaHandler).Assembly.GetType("Dfo.Server.Protocol.Game.RecoverStamina.GameRecoverStaminaCommand") is null;

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Real_decoder_sqlite_encoder_and_sender_preserve_order_and_no_effect_ledger(bool coin)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var f = await Create(coin, "success", delivery); var physical = delivery.Add(f.Connection);
        await Send(f, coin); await delivery.Settled();
        var frames = Split(physical.Sends).ToArray();
        Assert.Equal(coin ? new ushort[] { 0xCF, 0x0E, 0xB1 } : new ushort[] { 0x0E, 0x21, 9 }, frames.Select(Type));
        Assert.Equal(coin ? new byte[] { 1, 1 } : new byte[] { 1, 206, 169, 0, 0 }, frames.Single(x => Type(x) == (coin ? 0xCF : 9))[15..]);
        Assert.Equal(coin ? 100000 : 43470, f.Gold); Assert.Empty(f.Sender.Outbound.Closes);
        Assert.All(delivery.Reports, r => Assert.Equal(OutboundOutcome.Delivered, r.Outcome));
        NoLedger(f); Healthy(f); output.WriteLine($"coin={coin} physicalFrames={frames.Length} noLedger=True");
    }

    [Theory]
    [InlineData(false,"no-session")] [InlineData(false,"no-online")] [InlineData(false,"selected")]
    [InlineData(true,"no-session")] [InlineData(true,"no-online")] [InlineData(true,"selected")]
    public async Task Missing_identity_and_selected_not_world_preserve_original_empty_boundary_and_wire(bool coin,string mode)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace(); using var f = await Create(coin,mode,delivery,trace); var physical = delivery.Add(f.Connection);
        if(mode=="selected") TestGameplay.Run(f.Services,()=>
        {
            var sessions=f.Services.GetRequiredService<ISelectedSessionDomainService>();
            Assert.True(sessions.ReleaseExact(f.Session)); Assert.Null(sessions.Select(f.Session)); Assert.False(sessions.IsInWorld(f.Session));
        });
        await Send(f,coin); await delivery.Settled();
        var frames=Split(physical.Sends).ToArray();
        if(coin) { Assert.Equal(new ushort[] {0xCF,0xB1},frames.Select(Type)); Assert.All(frames,frame=>Assert.Equal(new byte[] {0},frame[15..])); }
        else if(mode=="no-online") Assert.Equal(new byte[] {0,4},Assert.Single(frames)[15..]); else Assert.Empty(frames);
        if(coin && mode=="selected") Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Transactions).Completion); else Assert.Empty(trace.Transactions);
        Assert.Equal(100000,f.Gold); Assert.Equal(10,f.Services.GetRequiredService<IWeaknessDomainService>().For(f.Session.CharacterId).Value);
        Assert.Equal(0,f.Scalar("SELECT count(*) FROM character_daily_counters")); Assert.Equal(0,physical.CloseCalls); NoLedger(f);
    }

    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Real_action_and_native_commit_errors_preserve_assets_and_original_response(bool coin, bool native)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace(); using var f = await Create(coin, "success", delivery, trace); var physical = delivery.Add(f.Connection);
        SqlFailure(f, coin, native);
        var error = await Record.ExceptionAsync(() => Send(f, coin).AsTask());
        var tx = Assert.Single(trace.Transactions);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, tx.Completion);
        Assert.Equal(100000, f.Gold); Assert.Equal(10, f.Services.GetRequiredService<IWeaknessDomainService>().For(f.Session.CharacterId).Value);
        Assert.Equal(0, f.Scalar("SELECT count(*) FROM character_daily_counters")); Assert.Equal(0, f.Scalar("SELECT coalesce(sum(count),0) FROM item_slots WHERE slot_index=1"));
        if (coin) { var sql = Assert.IsType<SqliteException>(error); Assert.Equal(native ? 787 : 1811, sql.SqliteExtendedErrorCode); await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1,physical.CloseCalls); Assert.Empty(physical.Sends); AssertCloseChain(f,trace); }
        else { Assert.Null(error); await delivery.Settled(); Assert.Equal(new byte[] {0,4},Assert.Single(Split(physical.Sends))[15..]); Assert.Equal(0,physical.CloseCalls); Healthy(f); }
        if (native) { var failure = Assert.IsType<SqliteException>(tx.CommitFailure!.Cause); Assert.Contains("SqliteTransaction.Commit", failure.StackTrace!); Assert.Equal(TransactionFailureKind.KnownStorage, tx.CommitFailure.Kind); }
        NoLedger(f); output.WriteLine($"coin={coin} native={native} completion={tx.Completion}");
    }

    [Theory] [InlineData("nonfallible")] [InlineData("rollback")] [InlineData("scope")] [InlineData("cancelled")] [InlineData("typed")]
    public async Task Recovery_unrecoverable_failure_is_not_an_ordinary_ack_and_keeps_empty_protection(string mode)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace(); using var f = await Create(false, mode, delivery, trace); var physical = delivery.Add(f.Connection);
        if (mode == "rollback") SqlFailure(f, false, false);
        var failure = await Record.ExceptionAsync(() => Send(f, false).AsTask());
        var tx = Assert.Single(trace.Transactions);
        Assert.Equal(mode == "scope" ? DomainTransactionCompletion.Committed : mode is "nonfallible" or "rollback" or "typed" ? DomainTransactionCompletion.Uncertain : DomainTransactionCompletion.ConfirmedRollback, tx.Completion);
        Assert.Equal(mode is "scope" or "nonfallible" ? 43470 : 100000, f.Gold);
        if (Legacy) { Assert.Null(failure); await delivery.Settled(); Assert.Equal(new byte[] { 0,4 }, Assert.Single(Split(physical.Sends))[15..]); Assert.DoesNotContain(delivery.Recorder.Entries,e=>e.IsClose); }
        else
        {
            Assert.NotNull(failure); Assert.True(Includes(failure, trace.Fault)); Assert.True(GameplayJob.EndedAJob(failure));
            if (mode == "rollback") Assert.Contains(Exceptions(failure), e => e is SqliteException { SqliteExtendedErrorCode: 1811 });
            Assert.DoesNotContain(f.Sender.Messages.OfType<RecoverStaminaResponse>(), r => r.ErrorCode != 0);
            await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled(); Assert.Equal(1, physical.CloseCalls); Assert.Equal("close", physical.Events.Last().Kind);
            if (mode == "scope") Assert.Equal(new ushort[] { 0x0E, 0x21, 9 }, Split(physical.Sends).Select(Type)); else Assert.Empty(physical.Sends);
            AssertCloseChain(f,trace);
        }
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
        NoLedger(f); output.WriteLine($"mode={mode} legacy={Legacy} completion={tx.Completion} sends={physical.Sends.Count} close={physical.CloseCalls} failure={failure}");
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Scope_failure_keeps_exact_source_close_and_replacement_connection_healthy(bool coin)
    {
        if (!coin && Legacy) return; // Recovery's old catch intentionally returned Error4 instead of closing.
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace(); using var f = await Create(coin, "scope", delivery, trace); var physical = delivery.Add(f.Connection);
        var replacement = f.Connection with { }; var next = delivery.Add(replacement);
        trace.BeforeScope = () => { f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId); f.Sender.Outbound.Connections.Add(replacement); };
        var failure = await Record.ExceptionAsync(() => Send(f, coin).AsTask());
        if(Legacy) Assert.Same(trace.Fault,failure); else { var typed=Assert.IsType<TransactionOutcomeUncertainException>(failure); Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure,typed.Reason); Assert.True(Includes(typed,trace.Fault)); }
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Transactions).Completion);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(1, physical.CloseCalls); Assert.Equal(0, next.CloseCalls); Assert.Empty(next.Events);
        Assert.Equal(Legacy ? Array.Empty<ushort>() : coin ? new ushort[] { 0xCF, 0x0E, 0xB1 } : new ushort[] { 0x0E, 0x21, 9 }, Split(physical.Sends).Select(Type));
        Assert.Equal("close", physical.Events.Last().Kind);
        AssertCloseChain(f,trace); Assert.False(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(replacement,out _));
        Assert.All(delivery.Recorder.Entries, e => Assert.Same(f.Connection, e.Connection));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
        Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
        NoLedger(f);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Projected_actual_encoder_failure_preserves_committed_assets_and_original_recipient_isolation(bool coin)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var f = await Create(coin, "success", delivery); var physical = delivery.Add(f.Connection);
        var fault = new IOException("TASK40 actual encoder"); var calls = 0;
        f.Sender.Outbound.Encoder.Fail((connection, _) => { Assert.Same(f.Connection, connection); calls++; return fault; });
        var failure = await Record.ExceptionAsync(() => Send(f, coin).AsTask());
        await delivery.Settled();
        Assert.Null(failure); Assert.Empty(delivery.Recorder.Entries);
        Assert.Equal(coin ? 100000 : 43470, f.Gold); Assert.Equal(coin ? 1 : 0, f.Scalar("SELECT coalesce(sum(count),0) FROM item_slots WHERE slot_index=1"));
        Assert.True(calls > 0); Assert.Empty(physical.Sends); Assert.Equal(0, physical.CloseCalls);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); NoLedger(f);
        output.WriteLine($"coin={coin} legacy={Legacy} encoderCalls={calls} failure={failure}");
    }

    [Fact]
    public async Task Requested_cancellation_rolls_back_and_closes_source_without_ordinary_ack()
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace(); using var f = await Create(false, "requested", delivery, trace); var physical = delivery.Add(f.Connection);
        var failure = await Record.ExceptionAsync(() => Send(f, false, ct: trace.Cancellation.Token).AsTask());
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, Assert.Single(trace.Transactions).Completion);
        Assert.Equal(100000, f.Gold); Assert.Equal(10, f.Services.GetRequiredService<IWeaknessDomainService>().For(f.Session.CharacterId).Value);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Empty(physical.Sends); Assert.Equal(1, physical.CloseCalls); NoLedger(f);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Direct_refusal_actual_encoder_failure_closes_exact_source_without_new_selected_protection(bool coin)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var f = await Create(coin, "success", delivery); var physical = delivery.Add(f.Connection);
        if (coin) await f.Seed(1,1,2,0);
        var fault = new IOException("TASK40 direct refusal encoder");
        f.Sender.Outbound.Encoder.Fail((connection,_) => { Assert.Same(f.Connection,connection); return fault; });
        var failure = await Record.ExceptionAsync(() => Send(f,coin,65535).AsTask());
        if(Legacy) Assert.Same(fault,failure); else { var typed=Assert.IsType<TransactionOutcomeUncertainException>(failure); Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure,typed.Reason); Assert.True(Includes(typed,fault)); }
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(1,physical.CloseCalls); Assert.Empty(physical.Sends); Assert.Equal(100000,f.Gold);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced); NoLedger(f);
        output.WriteLine($"coin={coin} legacy={Legacy} directEncoderFailure={failure}");
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Independent_request_failure_cannot_be_recovered_as_an_ordinary_result(bool coin)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace(); using var f = await Create(coin, "sticky", delivery, trace); var physical = delivery.Add(f.Connection);
        var failure = await Record.ExceptionAsync(() => Send(f, coin).AsTask());
        Assert.NotNull(failure); Assert.True(Includes(failure, trace.Fault));
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, Assert.Single(trace.Transactions).Completion);
        Assert.Equal(100000, f.Gold); Assert.Equal(0, f.Scalar("SELECT count(*) FROM character_daily_counters"));
        Assert.Equal(0, f.Scalar("SELECT coalesce(sum(count),0) FROM item_slots WHERE slot_index=1"));
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Empty(physical.Sends); Assert.Equal(1, physical.CloseCalls);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); NoLedger(f);
    }

    private static async Task<ShopFixture> Create(bool coin, string mode, RemainingOpeningProtocolCommandFlowTests.SenderProbe? delivery = null, Trace? trace = null)
    {
        trace ??= new Trace(); var t = trace; t.Coin = coin; t.Mode = mode;
        var f = new ShopFixture(services =>
        {
            if (delivery is not null) services.AddSingleton<IOutboundSender>(delivery.Recorder);
            services.AddSingleton(p => new GameplayOutboxes(p.GetRequiredService<IOutboundSender>(),
                p.GetRequiredService<GameplayCloseCauses>(),p.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GameplayOutbox>>()));
            var selected = services.Last(x => x.ServiceType == typeof(ISelectedSessionDomainService));
            services.AddSingleton<ISelectedSessionDomainService>(p => QueryObserver<ISelectedSessionDomainService>.Wrap(Resolve<ISelectedSessionDomainService>(selected,p),t));
            var online = services.Last(x => x.ServiceType == typeof(Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService));
            services.AddSingleton<Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService>(p => QueryObserver<Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService>.Wrap(Resolve<Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService>(online,p),t));
            var weakness = services.Last(x => x.ServiceType == typeof(IWeaknessDomainService));
            services.AddSingleton<IWeaknessDomainService>(p => new ObserveWeakness(Resolve<IWeaknessDomainService>(weakness,p), p, mode, t));
            var coins = services.Last(x => x.ServiceType == typeof(IReviveCoinDomainService));
            services.AddSingleton<IReviveCoinDomainService>(p => new ObserveCoins(Resolve<IReviveCoinDomainService>(coins,p),p,t));
            if (!Legacy)
            {
                var units = services.Last(x => x.ServiceType == typeof(ICommandWorkUnits));
                services.AddScoped<ICommandWorkUnits>(p => new ObserveUnits(Resolve<ICommandWorkUnits>(units,p),t));
            }
            if (mode == "scope")
            {
                var name = Legacy ? coin ? "ClaimDailyReviveCoinCommand" : "RecoverStaminaCommand" : coin ? "GameDailyReviveCoinCommand" : "GameRecoverStaminaCommand";
                var descriptor = services.Last(x => x.ServiceType.IsGenericType && x.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) && x.ServiceType.GenericTypeArguments[0].Name == name);
                services.AddScoped(descriptor.ServiceType, p => Activator.CreateInstance(typeof(ScopeFault<,>).MakeGenericType(descriptor.ServiceType.GenericTypeArguments), ActivatorUtilities.CreateInstance(p, descriptor.ImplementationType!),t)!);
            }
        });
        f.Sql($"UPDATE characters SET level=85,exp={f.ExperienceForLevel(85)} WHERE character_id=1");
        await TestGameplay.RunAsync(f.Services, async () => await f.Services.GetRequiredService<IWeaknessDomainService>().For(f.Session.CharacterId).SetWeakness());
        t.Session = f.Session; t.Armed = true; return f;
    }
    private static T Resolve<T>(ServiceDescriptor d,IServiceProvider p) where T:class => (T)(d.ImplementationInstance ?? d.ImplementationFactory?.Invoke(p) ?? ActivatorUtilities.CreateInstance(p,d.ImplementationType!));
    private static void SqlFailure(ShopFixture f,bool coin,bool native)
    {
        var table = coin ? "character_daily_counters" : "character_stats"; var verb = coin ? "INSERT" : "UPDATE OF carried_gold";
        f.Sql(native ? $"CREATE TABLE task40_parent(id INTEGER PRIMARY KEY); CREATE TABLE task40_child(id INTEGER REFERENCES task40_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER task40_fail AFTER {verb} ON {table} BEGIN INSERT INTO task40_child VALUES(404); END" : $"CREATE TRIGGER task40_fail BEFORE {verb} ON {table} BEGIN SELECT RAISE(ABORT,'TASK40 action'); END");
    }
    internal static async ValueTask Send(ShopFixture f,bool coin,ushort mode=0,CancellationToken ct=default)
    {
        ushort type = coin ? (ushort)0xCF : (ushort)9; byte[] body = coin ? [] : [(byte)mode,(byte)(mode >> 8)];
        var metadata = new GameMessageMetadata(1,type,0,7) { InboundOrdinal=1,SequenceIsFresh=true }.WithRawBody(body);
        var registration=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1 && x.Type==type);
        var message=((IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType)).Decode(metadata,body);
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),message!,ct);
    }
    private static void AssertCloseChain(ShopFixture f,Trace trace)
    {
        Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var cause));
        Assert.Equal(trace.Chain.Id,cause.Id); Assert.Equal(trace.Chain.RootId,cause.RootId);
        Assert.Equal(f.Connection.ConnectionId,cause.SourceConnection); Assert.Equal(GameplayCallSource.Connection,cause.Source);
    }
    private static void Healthy(ShopFixture f) { Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced); Assert.Empty(f.Sender.Outbound.Closes); }
    private static IEnumerable<Exception> Exceptions(Exception error) { yield return error; if(error is AggregateException a) foreach(var inner in a.InnerExceptions) foreach(var e in Exceptions(inner)) yield return e; else if(error.InnerException is {} cause) foreach(var e in Exceptions(cause)) yield return e; }
    private static bool Includes(Exception outer,Exception cause)=>ReferenceEquals(outer,cause)||outer is AggregateException a && a.InnerExceptions.Any(e=>Includes(e,cause))||outer.InnerException is {} inner && Includes(inner,cause);
    private static void NoLedger(ShopFixture f) { var table=typeof(EffectOperationLedger).GetField("ledgers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Services.GetRequiredService<EffectOperationLedger>())!; object?[] args=[f.Connection,null]; Assert.False((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,args)!); }
    private static ushort Type(byte[] frame)=>BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
    private static IEnumerable<byte[]> Split(IEnumerable<byte[]> sends) { foreach(var bytes in sends) { var offset=0; while(offset<bytes.Length) { var length=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset+3)); yield return bytes.AsSpan(offset,length).ToArray(); offset+=length; } } }
    public sealed class Trace { internal GameplayCallChain Chain; internal CommandExecutionContext Context = null!; internal Dfo.Server.Data.World.SelectedSessionIdentity Session = null!; internal bool Coin; internal string Mode = ""; internal readonly CancellationTokenSource Cancellation = new(); internal bool Armed; internal List<DomainTransaction> Transactions=[]; internal Exception Fault=new IOException("TASK40 independent failure"); internal Action? BeforeScope; }
    public sealed class ScopeFault<TCommand,TResult>(object actual,Trace trace):ICommandHandler<TCommand,TResult>,IDisposable where TCommand:class,ICommand<TResult> where TResult:notnull
    { public ValueTask<TResult> HandleAsync(CommandExecutionContext c,TCommand command,CancellationToken ct)=>((ICommandHandler<TCommand,TResult>)actual).HandleAsync(c,command,ct); public void Dispose() { trace.BeforeScope?.Invoke(); throw trace.Fault; } }
    private sealed class ObserveWeakness(IWeaknessDomainService actual,IServiceProvider p,string mode,Trace trace):IWeaknessDomainService
    {
        public IWeakness For(CharacterId id)=>new State(actual.For(id),p,mode,trace);
        public TimeSpan RecoveryInterval=>actual.RecoveryInterval;
        public IReadOnlyList<CharacterId> AdvanceRecovery()=>actual.AdvanceRecovery();
        public ValueTask<IReadOnlyList<CharacterId>> RetryPendingWeakness(CancellationToken ct)=>actual.RetryPendingWeakness(ct);
    }
    private sealed class State(IWeakness actual,IServiceProvider p,string mode,Trace trace):IWeakness
    {
        public byte Value=>actual.Value;
        public ValueTask<int> RecoveryGoldCost(CancellationToken ct=default)
        {
            if(trace.Armed) { var tx=Assert.IsType<DomainTransaction>(p.GetRequiredService<IDomainTransactionAccessor>().Current); trace.Transactions.Add(tx); trace.Chain = GameplayJob.Current!.Value; if(mode is "nonfallible" or "rollback") tx.Enlist(trace,()=>new Fault(mode,trace)); }
            return actual.RecoveryGoldCost(ct);
        }
        public ValueTask SetWeakness()=>actual.SetWeakness();
        public void Recover() { actual.Recover(); if(trace.Armed && mode == "sticky") trace.Context.Request!.Failed(trace.Fault); if(trace.Armed && mode is "cancelled" or "typed" or "requested") { if(mode=="requested") trace.Cancellation.Cancel(); trace.Fault=mode is "cancelled" or "requested" ? new OperationCanceledException("TASK40 cancellation") : new TransactionOutcomeUncertainException(TransactionUncertaintyReason.RollbackFailure,"TASK40 typed",trace.Fault); throw trace.Fault; } }
        public void PauseRecovery()=>actual.PauseRecovery(); public void ResumeRecovery()=>actual.ResumeRecovery();
    }
    private sealed class Fault(string mode,Trace trace):ITransactionParticipant
    { public bool CommitCanFail=>false; public ValueTask PrepareAsync(CancellationToken ct)=>ValueTask.CompletedTask; public ValueTask CommitAsync(CancellationToken ct)=> mode=="nonfallible" ? ValueTask.FromException(trace.Fault) : ValueTask.CompletedTask; public ValueTask RollbackAsync(CancellationToken ct) { if(mode=="rollback") { throw trace.Fault; } return ValueTask.CompletedTask; } }
    public class QueryObserver<T> : DispatchProxy where T:class
    {
        private T actual=null!; private Trace trace=null!;
        internal static T Wrap(T actual,Trace trace) { var proxy=Create<T,QueryObserver<T>>(); var observer=(QueryObserver<T>)(object)proxy; observer.actual=actual; observer.trace=trace; return proxy; }
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(trace.Armed && trace.Mode=="no-session" && method!.Name=="FindByConnection") return null;
            if(trace.Armed && trace.Mode=="no-online" && method!.Name=="TryGetByConnection") { args![1]=null; return false; }
            try { return method!.Invoke(actual,args); }
            catch(TargetInvocationException error) when(error.InnerException is {} cause) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(cause); throw; }
        }
    }
    private sealed class ObserveUnits(ICommandWorkUnits actual, Trace trace) : ICommandWorkUnits
    {
        private void Check(CommandExecutionContext context, CommandWorkUnitDeclaration declaration)
        {
            trace.Context = context;
            Assert.IsType<CommandRequestContext.ProtocolNonEffect>(context.Request);
            Assert.Empty(declaration.Participants); Assert.Equal(CommandWorkUnitKind.Ordinary, declaration.Kind);
            Assert.Equal(trace.Coin ? CommandFootprintMode.DeclaredNonRequest : CommandFootprintMode.None, declaration.Footprint);
            if(trace.Coin) Assert.Equal(trace.Session, Assert.Single(declaration.FootprintOwners!)); else Assert.Null(declaration.FootprintOwners);
        }
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> action,CancellationToken ct=default) where T:notnull
        { Check(c,d); Assert.False(trace.Coin); return actual.WriteResultAsync(c,d,action,ct); }
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> action,CancellationToken ct=default) where T:notnull
        { Check(c,d); Assert.True(trace.Coin); return actual.WriteAsync(c,d,action,ct); }
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> action,CancellationToken ct=default) where T:notnull => throw new InvalidOperationException("Recovery and daily coin each retain one ordinary writable boundary.");
    }
    private sealed class ObserveCoins(IReviveCoinDomainService actual,IServiceProvider p,Trace trace):IReviveCoinDomainService
    {
        public ValueTask<bool> ClaimedAsync(CharacterId id,CancellationToken ct)=>actual.ClaimedAsync(id,ct);
        public ValueTask<ItemUseApplication?> TryUseAsync(Dfo.Server.Data.World.SelectedSessionIdentity session,BusinessSlot source,ItemReference item,Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint,CancellationToken ct)=>actual.TryUseAsync(session,source,item,footprint,ct);
        public async ValueTask<DailyReviveCoinResult> ClaimAsync(Dfo.Server.Data.World.SelectedSessionIdentity session,Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint,CancellationToken ct)
        { trace.Chain = GameplayJob.Current!.Value; trace.Transactions.Add(Assert.IsType<DomainTransaction>(p.GetRequiredService<IDomainTransactionAccessor>().Current)); var result = await actual.ClaimAsync(session,footprint,ct); if(trace.Mode == "sticky") trace.Context.Request!.Failed(trace.Fault); return result; }
    }
}
