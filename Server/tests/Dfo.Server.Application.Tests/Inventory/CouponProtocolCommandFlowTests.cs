using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.QuestClearCoupon;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using SenderProbe = Dfo.Server.Application.Tests.Inventory.RemainingOpeningProtocolCommandFlowTests.SenderProbe;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class CouponProtocolCommandFlowTests(ITestOutputHelper output)
{
    private static bool Modern => typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.QuestClearCoupon.GameQuestClearCouponCommand") is not null;
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Three_actual_decoder_paths_commit_once_and_replay_current_state_on_original_entry(int branch)
    {
        using var f = await Fixture.Create(branch);
        var route = f.Host.Services.GetRequiredService<MessageHandlerRegistry>(); Assert.True(route.TryGet(ClientMessageType.GameQuestClearCoupon, out var binding));
        Assert.Equal((Environment.GetEnvironmentVariable("TASK52_EXPECT_ROUTE") ?? (Modern ? "whole" : "legacy")) != "legacy", binding!.ProtocolCommandType is not null);
        await EffectRequestTransport.Send(f.Host, f.Input);
        Assert.True(Assert.Single(f.Host.Sender.Messages.OfType<QuestClearCouponResponse>()).Succeeded);
        var frames=f.Host.Sender.Outbound.Frames; var types=frames.Select(frame=>BitConverter.ToUInt16(frame,1)).ToArray();
        output.WriteLine("initial frames="+string.Join(',',types.Select(type=>type.ToString("X4"))));
        Assert.Equal(Convert.FromHexString(branch==0?"014500002A000000":branch==1?"014500003A000000":"014500004D000000"),frames[0][15..]);
        Assert.Equal(branch==0 ? new ushort[]{0x204,0x0E,0x227} : branch==1 ? new ushort[]{0x204,0x0E,0x24,0x21E,0x21E,0xAC} : new ushort[]{0x204,0x0E,0x164,0x23F,0x15}, types);
        Assert.Equal(1, f.Host.Count(f.Item)); Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(f.Trace.Attempts).Transaction.Completion);
        if (branch == 0) Assert.Equal(4, (await f.Host.Services.GetRequiredService<IHerasDungeonDomainService>().ReadQuotaAsync(f.Host.Session.CharacterId, default)).Remaining);
        if (branch == 1) Assert.Equal(20, (await f.Host.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(f.Host.Session.CharacterId, default)).Used);
        if (branch == 2) Assert.Equal(1, f.Host.Scalar("SELECT flag_value FROM character_invisible_flags WHERE character_id=1 AND slot_index=101"));
        if(Modern)
        {
            var main=Assert.Single(f.Trace.Units,x=>x.Declaration.Kind==CommandWorkUnitKind.MainSettlement);Assert.False(main.ReadOnly);
            var actor=Assert.Single(main.Declaration.Participants);Assert.Equal(f.Host.Session,actor.Session);Assert.Same(f.Host.Connection,actor.Connection);
            var reads=f.Trace.Units.Where(x=>x.ReadOnly).ToArray();Assert.Equal(branch==1?1:0,reads.Length);
            foreach(var read in reads){Assert.Equal(CommandWorkUnitKind.Ordinary,read.Declaration.Kind);Assert.Empty(read.Declaration.Participants);Assert.Equal(CommandFootprintMode.None,read.Declaration.Footprint);}
        }
        var entry = Entry(f.Host); var ordinal = entry.Ordinal; var before = Assets(f);
        await EffectRequestTransport.Send(f.Host, f.Input);
        Assert.Same(entry, Entry(f.Host)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Equal(before, Assets(f)); Assert.Single(f.Trace.Attempts); Assert.Single(f.Host.Sender.Messages.OfType<QuestClearCouponResponse>());
        Assert.True(EffectRequestTransport.IsStateReplay(f.Host.Sender.Batches.Last()));
        output.WriteLine($"branch={branch} route={binding.ProtocolCommandType?.Name ?? "legacy"} ordinal={ordinal} committed=True assetsOnce=True");
    }
    [Theory] [InlineData(0,false)] [InlineData(0,true)] [InlineData(1,false)] [InlineData(1,true)] [InlineData(2,false)] [InlineData(2,true)]
    public async Task Actual_action_and_native_commit_failures_confirm_rollback_and_same_frame_can_retry(int branch, bool native)
    {
        using var f = await Fixture.Create(branch); var before = Assets(f);
        f.Host.Sql(native
            ? "CREATE TABLE coupon_parent(id INTEGER PRIMARY KEY); CREATE TABLE coupon_child(id INTEGER REFERENCES coupon_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER coupon_fault AFTER UPDATE OF count ON item_slots BEGIN INSERT INTO coupon_child VALUES(404); END"
            : "CREATE TRIGGER coupon_fault BEFORE UPDATE OF count ON item_slots BEGIN SELECT RAISE(ABORT,'coupon action'); END");
        await EffectRequestTransport.Send(f.Host, f.Input);
        var attempt = Assert.Single(f.Trace.Attempts); var cause = Assert.IsType<SqliteException>(native ? attempt.Transaction.CommitFailure!.Cause : attempt.Failure);
        Assert.Equal(native ? 787 : 1811, cause.SqliteExtendedErrorCode); Assert.Equal(native, attempt.Returned);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, attempt.Transaction.Completion); Assert.Equal(before, Assets(f));
        Assert.False(Assert.Single(f.Host.Sender.Messages.OfType<QuestClearCouponResponse>()).Succeeded);
        var entry = Entry(f.Host); var ordinal = entry.Ordinal; Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State);
        Assert.Empty(f.Host.Sender.Outbound.Closes); f.Host.Sql("DROP TRIGGER coupon_fault");
        await EffectRequestTransport.Send(f.Host, f.Input); Assert.Same(entry, Entry(f.Host)); Assert.Equal(ordinal, entry.Ordinal);
        Assert.Equal(EffectOperationState.Committed, entry.State); Assert.Equal(1, f.Host.Count(f.Item)); Assert.Equal(2, f.Trace.Attempts.Count);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Real_scope_disposal_keeps_committed_assets_and_observes_actual_physical_delivery(int branch)
    {
        await using var sender = new SenderProbe(); var trace = new Trace { ScopeFailure = new IOException("coupon actual DI Dispose"), Delivery = sender };
        using var f = await Fixture.Create(branch, trace); var physical = sender.Add(f.Host.Connection);
        var error = await Record.ExceptionAsync(() => EffectRequestTransport.Send(f.Host, f.Input).AsTask());
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Attempts).Transaction.Completion); Assert.Equal(1, f.Host.Count(f.Item));
        if (Modern)
        {
            var typed = Assert.IsType<TransactionOutcomeUncertainException>(error); Assert.Same(trace.ScopeFailure, typed.InnerException);
            await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await sender.Settled();
            Assert.True(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
            Assert.NotEmpty(physical.Sends); Assert.Equal((byte)1, physical.Sends.First()[15]);
            Assert.Equal("close", physical.Events.Last().Kind); Assert.Equal(1, physical.CloseCalls);
        }
        else
        {
            Assert.Null(error); await sender.Settled(); Assert.Equal(0, physical.CloseCalls);
            Assert.Empty(f.Host.Sender.Messages); Assert.Equal(new byte[] { 0, 2, 69, 0, 0, 0 }, Assert.Single(physical.Sends)[15..]);
            Assert.False(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        }
        Assert.Equal(EffectOperationState.Committed, Entry(f.Host).State);
        Assert.Equal(Modern, f.Host.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Host.Session)!.Fenced);
        if (Modern) Assert.Contains(f.Host.Session, trace.Discarded); else Assert.Empty(trace.Discarded);
        output.WriteLine($"branch={branch} modern={Modern} scope=actualDispose state={Entry(f.Host).State} sends={physical.Sends.Count} close={physical.CloseCalls}");
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Initial_projection_encoder_failure_remains_recipient_isolated_and_committed(int branch)
    {
        await using var sender = new SenderProbe(); using var f = await Fixture.Create(branch, new Trace { Delivery = sender });
        var physical = sender.Add(f.Host.Connection); var cause = new IOException("coupon initial actual encoder"); var calls = 0;
        f.Host.Sender.Outbound.Encoder.Fail((_, message) => message is QuestClearCouponResponse && calls++ == 0 ? cause : null);
        await EffectRequestTransport.Send(f.Host, f.Input); await sender.Settled();
        Assert.Equal(1, calls); Assert.Equal(1, f.Host.Count(f.Item)); Assert.Equal(EffectOperationState.Committed, Entry(f.Host).State);
        Assert.False(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        Assert.DoesNotContain(f.Host.Sender.Messages, m => m is QuestClearCouponResponse); Assert.Equal(0, physical.CloseCalls);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Actual_replay_encoder_failure_keeps_raw_cause_and_closes_original_physical_only(int branch)
    {
        await using var sender = new SenderProbe(); using var f = await Fixture.Create(branch, new Trace { Delivery = sender });
        var original = f.Host.Connection; var physical = sender.Add(original);
        await EffectRequestTransport.Send(f.Host, f.Input); await sender.Settled();
        var entry = Entry(f.Host); var ordinal = entry.Ordinal; var before = Assets(f); var sent = physical.Sends.Count;
        var replacement = original with { }; var other = sender.Add(replacement); var cause = new IOException("coupon actual replay encoder"); var calls = 0;
        f.Host.Sender.Outbound.Encoder.Fail((target, _) => { Assert.Same(original,target); calls++; f.Host.Sender.Outbound.Connections.Remove(original.ConnectionId); f.Host.Sender.Outbound.Connections.Add(replacement); return cause; });
        Assert.Same(cause, await Record.ExceptionAsync(() => EffectRequestTransport.Send(f.Host, f.Input).AsTask()));
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await sender.Settled();
        Assert.Equal(1,calls); Assert.Equal(sent,physical.Sends.Count); Assert.Equal(1,physical.CloseCalls); Assert.Equal(0,other.CloseCalls); Assert.Empty(other.Sends);
        Assert.Same(entry,Entry(f.Host)); Assert.Equal(ordinal,entry.Ordinal); Assert.Equal(EffectOperationState.Committed,entry.State); Assert.Equal(before,Assets(f)); Assert.Single(f.Trace.Attempts);
        Assert.False(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(original)); Assert.False(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
    }
    [Theory] [InlineData(0,false)] [InlineData(0,true)] [InlineData(1,false)] [InlineData(1,true)] [InlineData(2,false)] [InlineData(2,true)]
    public async Task All_cancellation_and_recursive_typed_failures_propagate_without_refusal(int branch,bool typed)
    {
        var root = new IOException("coupon typed cause"); var cause = typed ? (Exception)new AggregateException(new InvalidOperationException("nested",new TransactionOutcomeUncertainException(TransactionUncertaintyReason.RollbackFailure,"typed",root))) : new OperationCanceledException("unrequested cancellation");
        var trace = new Trace { DomainFailure = cause }; using var f = await Fixture.Create(branch,trace); var before = Assets(f);
        var failure = await Record.ExceptionAsync(() => EffectRequestTransport.Send(f.Host,f.Input).AsTask());
        Assert.NotNull(failure); Assert.True(Contains(failure,cause)); Assert.Equal(before,Assets(f)); Assert.Empty(f.Host.Sender.Messages); Assert.NotEmpty(f.Host.Sender.Outbound.Closes);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Unsupported_action_uses_actual_slot_classification_but_does_not_consume_other_items(int branch)
    {
        using var f = await Fixture.Create(branch);
        await EffectRequestTransport.Send(f.Host, f.Input with { Action = 76 });
        if (branch == 0) { Assert.Equal(1,f.Host.Count(f.Item)); Assert.Single(f.Trace.Attempts); }
        else { Assert.Equal(2,f.Host.Count(f.Item)); Assert.Empty(f.Trace.Attempts); Assert.Equal(new byte[]{0,2,69,0,0,0},Assert.Single(f.Host.Sender.Outbound.Frames)[15..]); }
    }
    [Fact]
    public async Task Independent_ItemUsage_ReadOnly_failure_preserves_Main_and_uses_default_source_failure()
    {
        await using var sender = new SenderProbe(); var trace = new Trace { UsageFailure = new IOException("independent ItemUsage read"), Delivery = sender };
        using var f = await Fixture.Create(1, trace); var physical = sender.Add(f.Host.Connection);
        var failure = await Record.ExceptionAsync(() => EffectRequestTransport.Send(f.Host, f.Input).AsTask());
        Assert.NotNull(failure); Assert.True(Contains(failure, trace.UsageFailure));
        Assert.Equal(1, trace.UsageReads); Assert.Equal(1, f.Host.Count(f.Item)); Assert.Equal(EffectOperationState.Committed, Entry(f.Host).State);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Attempts).Transaction.Completion);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await sender.Settled(); Assert.Equal(1, physical.CloseCalls);
        Assert.Equal(Modern, f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        Assert.Equal(Modern, f.Host.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Host.Session)!.Fenced);
        if (Modern) { Assert.IsType<TransactionOutcomeUncertainException>(failure); Assert.Contains(f.Host.Session, trace.Discarded); }
        else { Assert.Same(trace.UsageFailure, failure); Assert.Empty(trace.Discarded); }
        Assert.NotEmpty(physical.Sends); Assert.Equal("close", physical.Events.Last().Kind);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Fatigue_only_opening_rejections_refresh_usage_after_the_refusal(bool opening)
    {
        var trace=new Trace {DomainFailure=opening?new OpeningRejectedException(OpeningRejection.ItemUnusable,"refused potion"):new IOException("ordinary potion failure")};
        using var f=await Fixture.Create(1,trace);var before=Assets(f);
        await EffectRequestTransport.Send(f.Host,f.Input);
        Assert.Equal(before,Assets(f));Assert.Equal(opening?1:0,trace.UsageReads);
        Assert.Equal(new byte[]{0,2,69,0,0,0},f.Host.Sender.Outbound.Frames[0][15..]);Assert.Empty(f.Host.Sender.Outbound.Closes);
        Assert.Equal(EffectOperationState.ConfirmedRollback,Entry(f.Host).State);
    }
    private static bool Contains(Exception error,Exception cause) => ReferenceEquals(error,cause) || error.InnerException is {} inner && Contains(inner,cause) || error is AggregateException a && a.InnerExceptions.Any(e=>Contains(e,cause));
    private static string Assets(Fixture f) => string.Join(':', f.Host.Count(f.Item),f.Host.Scalar("SELECT COALESCE(SUM(value),0) FROM character_daily_counters"),f.Host.Scalar("SELECT COALESCE(SUM(flag_value),0) FROM character_invisible_flags"),f.Host.Scalar("SELECT count(*) FROM domain_events"),f.Host.Scalar("SELECT count(*) FROM domain_event_deliveries"));
    private static EffectOperationEntry Entry(ShopFixture f)
    {
        var table = typeof(EffectOperationLedger).GetField("ledgers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Services.GetRequiredService<EffectOperationLedger>())!;
        object?[] args=[f.Connection,null]; Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,args)!);
        return Assert.Single(((IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!).Values.Cast<EffectOperationEntry>());
    }
    public sealed class Trace { internal readonly List<Attempt> Attempts=[]; internal Exception? ScopeFailure,DomainFailure,UsageFailure; internal int UsageReads; internal SenderProbe? Delivery; internal CommandExecutionContext? Context; internal readonly List<SelectedSessionIdentity> Discarded=[]; internal readonly List<(bool ReadOnly,CommandWorkUnitDeclaration Declaration)> Units=[]; }
    internal sealed record Attempt(DomainTransaction Transaction) { internal bool Returned; internal Exception? Failure; }
    public class DomainObserver : DispatchProxy
    {
        internal object Actual=null!; internal IServiceProvider Services=null!; internal Trace Trace=null!;
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(method!.Name == "GetSnapshotAtAsync")
            {
                Trace.UsageReads++;
                if(Trace.UsageFailure is {} failure){Assert.True(((DomainTransaction)Services.GetRequiredService<IDomainTransactionAccessor>().Current!).IsReadOnly);throw failure;}
            }
            if(method.Name is not ("UseAsync" or "TryUseAsync")) return InvokeActual(method,args!);
            return GetType().BaseType!.GetMethod(nameof(Observe),BindingFlags.Instance|BindingFlags.NonPublic)!.MakeGenericMethod(method.ReturnType.GenericTypeArguments[0]).Invoke(this,[method,args]);
        }
        private object? InvokeActual(MethodInfo method,object?[] args) { try{return method.Invoke(Actual,args);}catch(TargetInvocationException ex) when(ex.InnerException is {} cause){ExceptionDispatchInfo.Capture(cause).Throw();throw;} }
        private async ValueTask<T> Observe<T>(MethodInfo method,object?[] args)
        {
            var a=new Attempt((DomainTransaction)Services.GetRequiredService<IDomainTransactionAccessor>().Current!); Trace.Attempts.Add(a);
            try { if(Trace.DomainFailure is {} failure)throw failure; var value=await (ValueTask<T>)InvokeActual(method,args)!;a.Returned=true;return value; }
            catch(Exception failure){a.Failure=failure;throw;}
        }
        internal static void Add<T>(IServiceCollection s,Trace trace) where T:class
        {
            var d=s.Last(x=>x.ServiceType==typeof(T)); s.AddSingleton<T>(p=>{var proxy=Create<T,DomainObserver>();var o=(DomainObserver)(object)proxy;o.Actual=d.ImplementationInstance??d.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,d.ImplementationType!);o.Services=p;o.Trace=trace;return proxy;});
        }
    }
    private sealed class ScopeObserver<T,R>(object actual,Trace trace):ICommandHandler<T,R>,IDisposable where T:class,ICommand<R> where R:notnull
    {
        public ValueTask<R> HandleAsync(CommandExecutionContext c,T input,CancellationToken ct) {trace.Context=c;return ((ICommandHandler<T,R>)actual).HandleAsync(c,input,ct);}
        public void Dispose(){if(trace.ScopeFailure is {} failure)throw failure;}
    }
    public class CreatureProbe:DispatchProxy
    {
        internal object Actual=null!;internal Trace Trace=null!;
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(method!.Name=="DiscardExact")Trace.Discarded.Add((SelectedSessionIdentity)args![0]!);
            try{return method.Invoke(Actual,args);}catch(TargetInvocationException error) when(error.InnerException is {} cause){ExceptionDispatchInfo.Capture(cause).Throw();throw;}
        }
    }
    private sealed class UnitProbe(ICommandWorkUnits actual,Trace trace):ICommandWorkUnits
    {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken t=default) where T:notnull {trace.Units.Add((false,d));return actual.WriteAsync(c,d,a,t);}
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken t=default) where T:notnull {trace.Units.Add((false,d));return actual.WriteResultAsync(c,d,a,t);}
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken t=default) where T:notnull {trace.Units.Add((true,d));return actual.ReadOnlyAsync(c,d,a,t);}
    }
    private static void ObserveScope(IServiceCollection s,Trace trace,int branch)
    {
        var name=Modern?"GameQuestClearCouponCommand":branch==0?"UseEntryAllowanceCommand":branch==1?"UseFatiguePotionCommand":"QuestClearCouponCommand";
        var command=typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.QuestClearCoupon."+name,true)!;
        var result=command.GetInterfaces().Single(x=>x.IsGenericType&&x.GetGenericTypeDefinition()==typeof(ICommand<>)).GenericTypeArguments[0];
        var contract=typeof(ICommandHandler<,>).MakeGenericType(command,result);var d=s.Last(x=>x.ServiceType==contract);
        s.AddScoped(contract,p=>Activator.CreateInstance(typeof(ScopeObserver<,>).MakeGenericType(command,result),d.ImplementationInstance??d.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,d.ImplementationType!),trace)!);
    }
    private sealed class Fixture(CeraShopFixture owner,int item,QuestClearCouponRequest input,Trace trace):IDisposable
    {
        internal ShopFixture Host=>owner.Host;internal int Item=item;internal QuestClearCouponRequest Input=input;internal Trace Trace=trace;
        public void Dispose()=>owner.Dispose();
        internal static async Task<Fixture> Create(int branch,Trace? given=null)
        {
            var trace=given??new Trace();var item=branch==0?490002847:branch==1?2600026:10006007;
            var owner=new CeraShopFixture(questProtocol:true,configure:s=>{
                var content=new Content();s.AddSingleton<IStackableActionDefinitionSource>(content);s.AddSingleton<IItemUsageRuleSource>(content);
                s.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((item,ItemUseRules.Potion(0))));
                s.AddSingleton<IQuestClearCouponRuleService,Selection>();s.AddSingleton<IQuestContentRuleService,Quests>();
                if(trace.Delivery is {} sender)s.AddSingleton<IOutboundSender>(sender.Recorder);
                if(branch==0)DomainObserver.Add<IEntryAllowanceItemDomainService>(s,trace);else if(branch==1)DomainObserver.Add<IFatiguePotionDomainService>(s,trace);else DomainObserver.Add<IQuestClearCouponSettlementDomainService>(s,trace);
                DomainObserver.Add<IItemUsageDomainService>(s,trace);
                ObserveScope(s,trace,branch);
                var units=s.Last(x=>x.ServiceType==typeof(ICommandWorkUnits));s.AddSingleton<ICommandWorkUnits>(p=>new UnitProbe((ICommandWorkUnits)(units.ImplementationInstance??units.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,units.ImplementationType!)),trace));
                var creature=s.Last(x=>x.ServiceType==typeof(ICreatureRuntimeDomainService));
                s.AddSingleton<ICreatureRuntimeDomainService>(p=>{var proxy=DispatchProxy.Create<ICreatureRuntimeDomainService,CreatureProbe>();var spy=(CreatureProbe)(object)proxy;spy.Actual=creature.ImplementationInstance??creature.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,creature.ImplementationType!);spy.Trace=trace;return proxy;});
            });
            owner.Content.Placements[item]=new(2,1000,new(BusinessInventorySpace.Consumable,0,55));
            await owner.Host.Seed(69,item,2);
            if(branch==1)await TestGameplay.RunAsync(owner.Host.Services,async()=>{await using var tx=owner.Host.Services.GetRequiredService<DomainTransactionScope>().Begin();await owner.Host.Services.GetRequiredService<ICharacterFatigueDomainService>().SpendAsync(owner.Host.Session.CharacterId,50,default);await tx.CommitAsync(default);});
            if(branch==2)owner.Host.Sql("INSERT OR IGNORE INTO character_condition(character_id,pc_room_state) VALUES(1,0); INSERT OR IGNORE INTO character_pvp(character_id) VALUES(1); INSERT OR IGNORE INTO character_settings(character_id) VALUES(1); INSERT OR IGNORE INTO character_expert_job(character_id) VALUES(1); INSERT INTO character_active_quests VALUES(1,0,101,9); INSERT INTO character_quest_notify_selections VALUES(1,2,101)");
            return new(owner,item,new(new(1,0x204,0,7),69,0,0,branch==0?42u:branch==1?58u:77u,0,0,true),trace);
        }
    }
    private sealed class Content:IStackableActionDefinitionSource,IItemUsageRuleSource
    {
        public ValueTask<StackableActionDefinition?> GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult<StackableActionDefinition?>(id==490002847?new("[add enter limit dungeonincount]",50,85,0,true){EntryDungeonId=3900,EntryIncrease=1}:new("[add fatigue]",1,85,0,true){FatigueRecovery=30});
        ValueTask<ItemUsageRule?> IItemUsageRuleSource.GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult<ItemUsageRule?>(new(1,10000,true));
    }
    private sealed class Selection:IQuestClearCouponRuleService
    {public bool IsCoupon(int itemId) => itemId == 10_006_007;
     public ValueTask<IReadOnlyList<int>> SelectAsync(int item,byte level,byte job,byte grow,IReadOnlyList<ClearedQuest> cleared,DateTimeOffset at,CancellationToken ct)=>ValueTask.FromResult<IReadOnlyList<int>>([101]);}
    private sealed class Quests:IQuestContentRuleService
    {
        public ValueTask<QuestDefinition?> FindAsync(int id,CancellationToken ct)=>ValueTask.FromResult<QuestDefinition?>(null);
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken ct)=>ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([]);
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken ct)=>ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
        public QuestParameters Parameters()=>throw new NotSupportedException();
    }
}
