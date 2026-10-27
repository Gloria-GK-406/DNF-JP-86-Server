using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.OnlineUsers;
using System.Reflection;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

// Same registered decoder/dispatcher, SQLite/run participant, actual encoder and production outbox
// on both routes. Only scope/SQL fault injection and physical transport are controlled.
public sealed class ReportProtocolCommandFlowTests(ITestOutputHelper output)
{
    private static readonly string[] Fine = ["GrantTutorialRewardCommand", "DecreaseDurabilityCommand", "ConsumeDungeonBuffCommand", "ReadPremiumSnapshotCommand"];
    private static readonly string[] Whole = ["GameTaggedIdReportCommand", "GameDecreaseDurabilityCommand", "GamePremiumServiceUsageReportCommand", "GamePremiumServiceUsageReportCommand"];
    private static bool Modern => typeof(ProtocolModule).Assembly.GetTypes().Any(t => t.Name == Whole[0]);

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Registered_report_keeps_real_assets_and_empty_ordinary_or_readonly_boundary(int protocol)
    {
        var trace = new Trace(); using var f = await Create(protocol, trace);
        await Send(f, protocol); Changed(f, protocol); Declaration(trace, protocol);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Transactions).Completion);
        Assert.Equal(protocol == 3, trace.Transactions.Single().IsReadOnly);
        Assert.Single(f.Sender.Outbound.Frames);
        Assert.Equal(protocol == 0 ? (ushort)0x8F : protocol == 1 ? (ushort)0x30 : (ushort)0x36F, Type(f.Sender.Outbound.Frames[0]));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        output.WriteLine($"loaded={typeof(ProtocolModule).Assembly.Location} mvid={typeof(ProtocolModule).Module.ModuleVersionId} modern={Modern} protocol={protocol}");
    }

    [Theory] [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)] [InlineData(2, false)] [InlineData(2, true)]
    public async Task Real_action_and_native_commit_errors_rollback_assets_and_run_marker_without_ACK(int protocol, bool native)
    {
        var trace = new Trace { SqlFault = true }; using var f = await Create(protocol, trace);
        f.Sql(native ? "CREATE TABLE task55_parent(id INTEGER PRIMARY KEY); CREATE TABLE task55_fault(id INTEGER REFERENCES task55_parent(id) DEFERRABLE INITIALLY DEFERRED)"
            : "CREATE TABLE task55_fault(id INTEGER); CREATE TRIGGER task55_abort BEFORE INSERT ON task55_fault BEGIN SELECT RAISE(ABORT,'TASK55 action'); END");
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask());
        var tx = Assert.Single(trace.Transactions); var cause = Assert.IsType<SqliteException>(native ? tx.CommitFailure!.Cause : failure);
        Assert.Same(cause, failure); Assert.Equal(native ? 787 : 1811, cause.SqliteExtendedErrorCode);
        if (native) SqliteFailureAssertions.FromCommit(tx, cause);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, tx.Completion); Assert.Empty(f.Sender.Outbound.Frames);
        if (protocol == 0) { Assert.Equal(0, f.Count(8474)); Assert.Equal(0, f.Scalar("SELECT tutorial_completed FROM characters WHERE character_id=1")); }
        if (protocol == 1) Assert.Equal((ushort)2, await f.Durability(InventoryContainerType.Equipment, 12));
        if (protocol == 2) { Assert.Equal(0, f.Scalar("SELECT count(*) FROM character_daily_counters WHERE counter_key='premium_dungeon_buff_used'")); TestGameplay.Run(f.Services, () => Assert.False(f.Services.GetRequiredService<IDungeonRunDomainService>().HasUsedDungeonBuff(f.Session.CharacterId, trace.Run))); }
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        trace.SqlFault = false; await Send(f, protocol); Changed(f, protocol);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Actual_scope_Dispose_preserves_completion_and_frames_without_adding_selected_protection(int protocol)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace { ScopeFailure = new IOException("TASK55 actual DI Dispose") }; using var f = await Create(protocol, trace, delivery);
        var physical = delivery.Add(f.Connection);
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask());
        if (Modern && protocol != 3) Assert.Same(trace.ScopeFailure, Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);
        else Assert.Same(trace.ScopeFailure, failure);
        Changed(f, protocol); Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Transactions).Completion);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(Modern ? 1 : 0, Split(physical.Sends).Count()); Assert.Equal(1, physical.CloseCalls);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
        Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection, out var chain)); Assert.Equal(trace.Chain.RootId, chain.RootId);
        Declaration(trace, protocol);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task First_actual_encoder_failure_and_same_ID_physical_replacement_keep_original_failure_boundary(int protocol)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace(); using var f = await Create(protocol, trace, delivery); var physical = delivery.Add(f.Connection);
        var replacement = f.Connection with { }; var next = delivery.Add(replacement); var cause = new IOException("TASK55 actual encoder"); var calls = 0;
        f.Sender.Outbound.Encoder.Fail((target, _) => { calls++; Assert.Same(f.Connection, target); f.Sender.Outbound.Connections.Remove(target.ConnectionId); f.Sender.Outbound.Connections.Add(replacement); return cause; });
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask());
        // Durability's original ProjectAsync Failed result is deliberately ignored.
        if (protocol == 1) Assert.Null(failure);
        else if (Modern && protocol != 3) Assert.Same(cause, Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);
        else Assert.Same(cause, failure);
        Changed(f, protocol); Assert.Equal(1, calls); Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Transactions).Completion);
        if (protocol != 1) await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await delivery.Settled(); Assert.Empty(physical.Sends); Assert.Equal(protocol == 1 ? 0 : 1, physical.CloseCalls); Assert.Empty(next.Events); Assert.Equal(0, next.CloseCalls);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
    }

    [Fact]
    public async Task Completion_with_zero_reward_flag_still_writes_once_and_empty_noncompletion_steps_still_open_a_transaction()
    {
        var trace = new Trace(); using var f = await Create(0, trace);
        await Send(f, 0, [0, 31, 0, 0, 0, 0]); Assert.Equal(1, f.Scalar("SELECT tutorial_completed FROM characters WHERE character_id=1")); Assert.Equal(0, f.Count(8474));
        await Send(f, 0, [0, 77, 0, 0, 0, 0]); Assert.Equal(2, trace.Transactions.Count); Assert.Equal(2, f.Sender.Outbound.Frames.Count);
        Assert.Equal(1, f.Scalar("SELECT level FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Captured_old_run_does_not_consume_and_the_next_report_consumes_current_run()
    {
        var trace=new Trace(); using var f=await Create(2,trace);var old=trace.Run;DungeonRunId next=default;
        trace.BeforeWorld=()=> { trace.BeforeWorld=null; var runs=f.Services.GetRequiredService<IDungeonRunDomainService>();
            f.InTransaction(()=> {runs.End(f.Session.CharacterId,old);next=runs.Begin(f.Session.CharacterId,new DungeonLayout(144,0,1,1,new(0,0),new(0,0),55611,0,new Dictionary<MazeCell,DungeonCellDefinition>{[new(0,0)]=DungeonCellDefinition.Fixed(55611)}),difficulty:0).RunId;return true;}); };
        await Send(f,2);Assert.NotEqual(old,next);
        Assert.Equal(0,f.Scalar("SELECT count(*) FROM character_daily_counters WHERE counter_key='premium_dungeon_buff_used'"));
        TestGameplay.Run(f.Services,()=> {var runs=f.Services.GetRequiredService<IDungeonRunDomainService>();Assert.Equal(next,runs.Find(f.Session.CharacterId)!.RunId);Assert.False(runs.HasUsedDungeonBuff(f.Session.CharacterId,next));});
        await Send(f,2);Assert.Equal(1,f.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_dungeon_buff_used'"));
        TestGameplay.Run(f.Services,()=>Assert.True(f.Services.GetRequiredService<IDungeonRunDomainService>().HasUsedDungeonBuff(f.Session.CharacterId,next)));
    }

    [Fact]
    public async Task Unattributed_selected_report_still_grants_and_declaration_is_read_before_transaction()
    {
        var trace=new Trace{Unattributed=true};using var f=await Create(0,trace);
        await Send(f,0);Changed(f,0);Assert.Equal(1,trace.Declarations);Assert.Single(trace.Transactions);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Missing_selection_keeps_original_empty_ACK_error_or_silent_exit(int protocol)
    {
        var trace=new Trace();using var f=await Create(protocol,trace);
        TestGameplay.Run(f.Services,()=>f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session));
        await Send(f,protocol);Assert.Empty(trace.Transactions);Assert.Empty(trace.Units);
        if(protocol<2)Assert.Single(f.Sender.Outbound.Frames);else Assert.Empty(f.Sender.Outbound.Frames);
        if(protocol==0)Assert.Equal(new byte[]{1,0},f.Sender.Outbound.Frames[0][15..]);
        if(protocol==1)Assert.Equal(new byte[]{0,17},f.Sender.Outbound.Frames[0][15..]);
    }

    private static async Task<ShopFixture> Create(int protocol, Trace trace, RemainingOpeningProtocolCommandFlowTests.SenderProbe? delivery = null)
    {
        var f = new ShopFixture(s => {
            s.AddSingleton<ITutorialRewardDefinitionSource>(new Rewards());
            if (delivery is not null) s.AddSingleton<IOutboundSender>(delivery.Recorder);
            s.AddSingleton(p => new GameplayOutboxes(p.GetRequiredService<IOutboundSender>(), p.GetRequiredService<GameplayCloseCauses>(), p.GetRequiredService<ILogger<GameplayOutbox>>()));
            var rewardDescriptor=s.Last(x=>x.ServiceType==typeof(ITutorialRewardDomainService));
            s.AddSingleton<ITutorialRewardDomainService>(p=>new RewardProbe((ITutorialRewardDomainService)Actual(rewardDescriptor,p),p,trace));
            var onlineDescriptor=s.Last(x=>x.ServiceType==typeof(IOnlineUserQueryDomainService));
            s.AddSingleton<IOnlineUserQueryDomainService>(p=> {var proxy=DispatchProxy.Create<IOnlineUserQueryDomainService,OnlineProbe>();var spy=(OnlineProbe)(object)proxy;spy.Actual=Actual(onlineDescriptor,p);spy.Trace=trace;return proxy;});
            var sessionDescriptor=s.Last(x=>x.ServiceType==typeof(ISelectedSessionDomainService));
            s.AddSingleton<ISelectedSessionDomainService>(p=> {var proxy=DispatchProxy.Create<ISelectedSessionDomainService,SessionProbe>();var spy=(SessionProbe)(object)proxy;spy.Actual=Actual(sessionDescriptor,p);spy.Trace=trace;return proxy;});
            var name = Modern ? Whole[protocol] : Fine[protocol]; var d = s.Last(x => x.ServiceType.IsGenericType && x.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) && x.ServiceType.GenericTypeArguments[0].Name == name);
            s.AddScoped(d.ServiceType, p => Activator.CreateInstance(typeof(ScopeProbe<,>).MakeGenericType(d.ServiceType.GenericTypeArguments), Actual(d, p), trace, p)!);
            var u = s.Last(x => x.ServiceType == typeof(ICommandWorkUnits)); s.AddSingleton<ICommandWorkUnits>(p => new UnitProbe((ICommandWorkUnits)Actual(u, p), trace, p));
        });
        await f.SeedCore(12, StackableItemCore.Create(1,3000,1234) with { Durability = 2 }, InventoryContainerType.Equipment);
        f.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,583,4000000000)");
        trace.Run = f.InTransaction(() => f.Services.GetRequiredService<IDungeonRunDomainService>().Begin(f.Session.CharacterId, new DungeonLayout(144,0,1,1,new(0,0),new(0,0),55611,0,new Dictionary<MazeCell,DungeonCellDefinition> { [new(0,0)] = DungeonCellDefinition.Fixed(55611) }), difficulty:0)).RunId;
        trace.Armed = true; return f;
    }
    private static object Actual(ServiceDescriptor d,IServiceProvider p) => d.ImplementationInstance ?? d.ImplementationFactory?.Invoke(p) ?? ActivatorUtilities.CreateInstance(p,d.ImplementationType!);
    private static ushort Wire(int p) => p == 0 ? (ushort)0x8F : p == 1 ? (ushort)0x30 : (ushort)0x36F;
    private static ValueTask Send(ShopFixture f,int p,byte[]? body=null) => EffectRequestTransport.SendRaw(f,new(1,Wire(p),0,7),body ?? (p == 0 ? [0,31,0,0,0,1] : p == 1 ? [12] : [1,0,(byte)(p == 2 ? 3 : 4),0,..new byte[72]]));
    private static void Changed(ShopFixture f,int p) { if(p==0) {Assert.Equal(1,f.Count(8474));Assert.Equal(1,f.Scalar("SELECT tutorial_completed FROM characters WHERE character_id=1"));Assert.Equal(1,f.Scalar("SELECT level FROM characters WHERE character_id=1"));} if(p==1) Assert.Equal((ushort)1,f.Durability(InventoryContainerType.Equipment,12).AsTask().GetAwaiter().GetResult()); if(p==2) Assert.Equal(1,f.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_dungeon_buff_used'")); if(p==3) Assert.Equal(0,f.Scalar("SELECT count(*) FROM character_daily_counters WHERE counter_key='premium_dungeon_buff_used'")); }
    private static void Declaration(Trace trace,int p) {if(!Modern)return;var d=Assert.Single(trace.Units);Assert.Equal(CommandWorkUnitKind.Ordinary,d.Kind);Assert.Equal(CommandFootprintMode.None,d.Footprint);Assert.Empty(d.Participants);}
    private static ushort Type(byte[] b)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(1));
    private static IEnumerable<byte[]> Split(IEnumerable<byte[]> sends) {foreach(var bytes in sends)for(var i=0;i<bytes.Length;){var n=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i+3));yield return bytes[i..(i+n)];i+=n;}}
    private sealed class Rewards:ITutorialRewardDefinitionSource {public ValueTask<IReadOnlyList<TutorialRewardItem>> GetAsync(uint flag,CancellationToken ct)=>ValueTask.FromResult<IReadOnlyList<TutorialRewardItem>>([new(8474,1)]);}
    public sealed class Trace {internal bool Armed,SqlFault,Unattributed;internal int Declarations;internal Action? BeforeWorld;internal Exception? ScopeFailure;internal DungeonRunId Run;internal GameplayCallChain Chain;internal List<DomainTransaction> Transactions=[];internal List<CommandWorkUnitDeclaration> Units=[];}
    public sealed class ScopeProbe<T,R>(object actual,Trace trace,IServiceProvider p):ICommandHandler<T,R>,IDisposable where T:class,ICommand<R> where R:notnull {
        public async ValueTask<R> HandleAsync(CommandExecutionContext c,T q,CancellationToken ct) {trace.Chain=GameplayJob.Current!.Value;if(Modern)return await ((ICommandHandler<T,R>)actual).HandleAsync(c,q,ct);trace.Transactions.Add((DomainTransaction)p.GetRequiredService<IDomainTransactionAccessor>().Current!);var r=await ((ICommandHandler<T,R>)actual).HandleAsync(c,q,ct);await Fault(trace,p);return r;}
        public void Dispose(){if(trace.ScopeFailure is {} cause)throw cause;}
    }
    private static async ValueTask Fault(Trace trace,IServiceProvider p){if(trace.SqlFault)await p.GetRequiredService<SqliteDatabase>().ExecuteAsync("INSERT INTO task55_fault VALUES(404)",_=>{},default);}
    private sealed class RewardProbe(ITutorialRewardDomainService actual,IServiceProvider p,Trace trace):ITutorialRewardDomainService {public ValueTask<IReadOnlyList<TutorialRewardItem>> DeclaredForAsync(uint flag,CancellationToken ct){Assert.Null(p.GetRequiredService<IDomainTransactionAccessor>().Current);trace.Declarations++;return actual.DeclaredForAsync(flag,ct);}}
    public class OnlineProbe:DispatchProxy {internal object Actual=null!;internal Trace Trace=null!;protected override object? Invoke(MethodInfo? method,object?[]? args){if(method!.Name=="TryGetByConnection"&&Trace.Armed&&Trace.Unattributed){args![1]=null;return false;}return method.Invoke(Actual,args);}}
    public class SessionProbe:DispatchProxy {internal object Actual=null!;internal Trace Trace=null!;protected override object? Invoke(MethodInfo? method,object?[]? args){if(method!.Name=="IsInWorld"&&Trace.Armed)Trace.BeforeWorld?.Invoke();try{return method.Invoke(Actual,args);}catch(TargetInvocationException ex)when(ex.InnerException is {} cause){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw();throw;}}}
    private sealed class UnitProbe(ICommandWorkUnits actual,Trace trace,IServiceProvider p):ICommandWorkUnits {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull {trace.Units.Add(d);return actual.WriteAsync(c,d,async(u,t)=>{trace.Transactions.Add((DomainTransaction)p.GetRequiredService<IDomainTransactionAccessor>().Current!);var r=await a(u,t);await Fault(trace,p);return r;},ct);}
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull {trace.Units.Add(d);return actual.ReadOnlyAsync(c,d,t=>{trace.Transactions.Add((DomainTransaction)p.GetRequiredService<IDomainTransactionAccessor>().Current!);return a(t);},ct);}
    }
}
