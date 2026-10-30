using System.Collections;
using System.Buffers.Binary;
using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Game;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Abstractions.Settlement;

using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Characters;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;


namespace Dfo.Server.Application.Tests.Quests;

// Real registered decoder -> dispatcher -> actual command scope and SQLite -> production outbox/encoder.
// Published entry content and physical transport are controlled. Domain algorithms keep their own strong tests.
public sealed class QuestProtocolCommandFlowTests(ITestOutputHelper output)
{
    private static readonly string[] Fine = ["AcceptQuestCommand", "GiveUpQuestCommand", "SetQuestTriggerCommand", "FinishQuestCommand"];
    private static readonly string[] Whole = ["GameAcceptQuestCommand", "GameGiveUpQuestCommand", "GameSetQuestTriggerCommand", "GameFinishQuestCommand"];
    private static bool Modern => typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.QuestAcceptance.GameAcceptQuestCommand") is not null;

    [Fact]
    public void Actual_DI_has_four_unique_routes_and_no_legacy_fine_or_extra_raw_authority()
    {
        using var f=new ShopFixture();var rows=f.Services.GetServices<CommandHandlerRegistration>().ToArray();
        foreach(var name in Fine){Assert.DoesNotContain(rows,x=>x.CommandType.Name==name);Assert.Null(typeof(ProtocolModule).Assembly.GetTypes().SingleOrDefault(x=>x.Name==name||x.Name==name+"Handler"));}
        if(!Modern)return;
        for(var p=0;p<4;p++){
            var row=Assert.Single(rows,x=>x.CommandType.Name==Whole[p]);Assert.Equal(CommandRouteKind.Protocol,row.Route);var binding=Assert.Single(row.ProtocolMessages!);Assert.Equal((ClientMessageType)(65567+p),binding.MessageType);Assert.Equal(Fine[p].Replace("Command","Request"),binding.MessageClrType.Name);
            Assert.Equal(p==3?new[]{CommandRegistrationExit.CommittedReplayCurrentStateBatch}:[],row.RegistrationExits??[]);
        }
        Assert.Equal(ReplayProjectionNeeds.Inventory|ReplayProjectionNeeds.Quests,new Dfo.Server.Protocol.Game.QuestCompletion.FinishQuestRequest(GameMessageMetadata.Empty,2001,ushort.MaxValue,1).MinimumReplayNeeds);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task A_selection_that_has_not_entered_the_world_changes_no_quest_state(int protocol)
    {
        // Accept, give up and trigger refuse through their own refusal ACK; no work unit begins.
        var trace=new Trace();using var f=await Create(protocol,trace);var before=Assets(f);
        TestGameplay.Run(f.Services,()=>
        {
            var sessions=f.Services.GetRequiredService<ISelectedSessionDomainService>();
            Assert.True(sessions.ReleaseExact(f.Session));Assert.Null(sessions.Select(f.Session));Assert.False(sessions.IsInWorld(f.Session));
        });
        await Send(f,protocol);
        Assert.Equal(before,Assets(f));Assert.Empty(trace.Units);Assert.Empty(trace.Domain.Attempts);Assert.Single(f.Sender.Outbound.Frames);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Four_wire_routes_commit_once_with_original_state_and_frame_order(int protocol)
    {
        var trace=new Trace();using var f=await Create(protocol,trace);
        await Send(f,protocol);
        Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Domain.Attempts).Transaction.Completion);
        AssertChange(f,protocol);
        var wire=f.Sender.Outbound.Frames;
        Assert.Equal(ExpectedFrames(protocol),wire.Select(Type));
        Assert.Equal(1,wire[0][15]);
        AssertDeclaration(f,trace,protocol);
        ClosedMainRequest(f,trace,protocol);
        output.WriteLine($"route={Fine[protocol]} modern={Modern} completion=Committed frames={string.Join(',',wire.Select(Type))}");
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Actual_scope_dispose_failure_preserves_committed_facts_and_closes_only_original_source(int protocol)
    {
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace=new Trace{ScopeFailure=new IOException("TASK51 actual scope Dispose")};using var f=await Create(protocol,trace,delivery);var physical=delivery.Add(f.Connection);
        var failure=await Record.ExceptionAsync(()=>Send(f,protocol).AsTask());
        if(Modern) Assert.Same(trace.ScopeFailure,Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);
        else Assert.Same(trace.ScopeFailure,failure);
        Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Domain.Attempts).Transaction.Completion);AssertChange(f,protocol);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();Assert.Equal(1,physical.CloseCalls);CloseChain(f,trace);
        var frames=Split(physical.Sends).ToArray();
        if(Modern) Assert.Equal(ExpectedFrames(protocol),frames.Select(Type));else Assert.Empty(frames);
        Assert.Equal(Modern,f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal(Modern?new[]{f.Session}:[],trace.Discarded);
        Assert.Equal(Modern,f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
        AssertDeclaration(f,trace,protocol);ClosedMainRequest(f,trace,protocol);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task First_actual_encoder_failure_keeps_commit_and_protects_the_declared_actor(int protocol)
    {
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace=new Trace();using var f=await Create(protocol,trace,delivery);var physical=delivery.Add(f.Connection);
        var cause=new IOException("TASK51 actual first encoder");f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);return cause;});
        var failure=await Record.ExceptionAsync(()=>Send(f,protocol).AsTask());
        if(Modern) Assert.Same(cause,Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);else Assert.Same(cause,failure);
        AssertChange(f,protocol);Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Domain.Attempts).Transaction.Completion);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();Assert.Equal(1,physical.CloseCalls);Assert.Empty(physical.Sends);CloseChain(f,trace);
        Assert.Equal(Modern,f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal(Modern?new[]{f.Session}:[],trace.Discarded);
        Assert.Equal(Modern,f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
        if(protocol==3)Assert.Equal(EffectOperationState.Committed,Entry(f).State);AssertDeclaration(f,trace,protocol);
    }

    [Fact]
    public async Task Finish_committed_replay_keeps_entry_ordinal_and_actual_raw_encoder_replacement_isolated()
    {
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();var trace=new Trace();using var f=await Create(3,trace,delivery);var physical=delivery.Add(f.Connection);
        await Send(f,3);await delivery.Settled();var after=Assets(f);var entry=Entry(f);var ordinal=entry.Ordinal;
        await Send(f,3);await delivery.Settled();Assert.Single(trace.Domain.Attempts);Assert.Equal(after,Assets(f));Assert.Same(entry,Entry(f));Assert.Equal(ordinal,entry.Ordinal);
        Assert.Contains(delivery.Recorder.Entries.Last().Messages,m=>m.GetType().Name.Contains("InventorySnapshot"));
        var replacement=f.Connection with {};var next=delivery.Add(replacement);var cause=new IOException("TASK51 replay raw encoder");GameplayCallChain replay=default;var count=physical.Sends.Count;
        f.Sender.Outbound.Encoder.Fail((target,message)=>{replay=GameplayJob.Current!.Value;Assert.Same(f.Connection,target);Assert.Contains("InventorySnapshot",message.GetType().Name);f.Sender.Outbound.Connections.Remove(target.ConnectionId);f.Sender.Outbound.Connections.Add(replacement);return cause;});
        Assert.Same(cause,await Record.ExceptionAsync(()=>Send(f,3).AsTask()));await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();
        Assert.Equal(1,physical.CloseCalls);Assert.Equal(count,physical.Sends.Count);Assert.Equal(0,next.CloseCalls);Assert.Empty(next.Events);Assert.Same(next,delivery.Resolve(replacement));
        Assert.Equal(after,Assets(f));Assert.Same(entry,Entry(f));Assert.Equal(ordinal,entry.Ordinal);Assert.Equal(EffectOperationState.Committed,entry.State);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));CloseChain(f,replay);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Finish_action_late_SQL_is_ACK22_but_native_commit_is_never_a_business_refusal(bool native)
    {
        var trace=new Trace();using var f=await Create(3,trace);var before=Assets(f);
        f.Sql(native?"CREATE TABLE task51_parent(id INTEGER PRIMARY KEY); CREATE TABLE task51_child(id INTEGER REFERENCES task51_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER task51_fail AFTER UPDATE OF exp ON characters BEGIN INSERT INTO task51_child VALUES(404); END":"CREATE TRIGGER task51_fail BEFORE UPDATE OF exp ON characters BEGIN SELECT RAISE(ABORT,'TASK51 late action'); END");
        var failure=await Record.ExceptionAsync(()=>Send(f,3).AsTask());var attempt=Assert.Single(trace.Domain.Attempts);
        var cause=Assert.IsType<SqliteException>(native?attempt.Transaction.CommitFailure!.Cause:attempt.Failure);
        Assert.Equal(native?787:1811,cause.SqliteExtendedErrorCode);Assert.Equal(native,attempt.Returned);Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,attempt.Transaction.Completion);Assert.Equal(before,Assets(f));
        var entry=Entry(f);var ordinal=entry.Ordinal;Assert.Equal(EffectOperationState.ConfirmedRollback,entry.State);
        if(native){Assert.Same(cause,failure);SqliteFailureAssertions.FromCommit(attempt.Transaction, cause);Assert.Empty(f.Sender.Messages);}
        else {Assert.Null(failure);Assert.Equal(new byte[]{0,22},Assert.Single(f.Sender.Outbound.Frames)[15..]);}
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        // Logical confirmed-rollback retry: no physically closed transport is presented as a new network request.
        f.Sql("DROP TRIGGER task51_fail");await Send(f,3);Assert.Same(entry,Entry(f));Assert.Equal(ordinal,entry.Ordinal);Assert.Equal(EffectOperationState.Committed,entry.State);AssertChange(f,3);Assert.Equal(2,trace.Domain.Attempts.Count);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Finish_cancellation_and_recursive_uncertainty_keep_default_failure(bool typed)
    {
        Exception cause=typed?new AggregateException(new IOException("outer"),new TransactionOutcomeUncertainException(TransactionUncertaintyReason.RollbackFailure,"TASK51 typed",new IOException("inner"))):new OperationCanceledException("TASK51 cancel");
        var trace=new Trace();trace.Domain.DomainFailure=cause;using var f=await Create(3,trace);var before=Assets(f);
        Assert.Same(cause,await Record.ExceptionAsync(()=>Send(f,3).AsTask()));Assert.Equal(before,Assets(f));Assert.Empty(f.Sender.Messages);
        Assert.Equal(typed?DomainTransactionCompletion.Uncertain:DomainTransactionCompletion.ConfirmedRollback,Assert.Single(trace.Domain.Attempts).Transaction.Completion);
    }

    [Fact]
    public async Task Advancement_real_settlement_publishes_skills_before_ACK_then_identity_inventory_and_weakness()
    {
        var trace=new Trace();using var f=await Create(3,trace,growth:true);
        await Send(f,3);
        Assert.Equal(1,f.Scalar("SELECT grow_type FROM characters WHERE character_id=1"));
        Assert.Equal(3,f.Scalar("SELECT level FROM character_skills WHERE character_id=1 AND skill_id=7 AND page_index=0"));
        var frames=f.Sender.Outbound.Frames;output.WriteLine("advancement frames="+string.Join(',',frames.Select(Type)));
        Assert.Equal(new ushort[]{0x13,0x22,0x25,2,2,0x0D,0x15,0x21},frames.Select(Type));
        Assert.Equal((ushort)0x21,Type(frames[^1]));
        Assert.Contains(frames,x=>Type(x)==0x0D);Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Domain.Attempts).Transaction.Completion);
    }

    private static async Task<ShopFixture> Create(int protocol,Trace trace,RemainingOpeningProtocolCommandFlowTests.SenderProbe? delivery=null,bool growth=false)
    {
        var rules=new OrdinaryQuestEffectTests.Rules();rules.Quest=rules.Quest with {InitialTrigger=protocol==2?1u:0u,EventItems=protocol==0?[new(2000,2)]:[]};
        if(growth)rules.Quest=rules.Quest with {Rewards=[new(QuestRewardKind.GrowType,[1])]};
        var learning=new LearningContent();if(growth)learning.Grants.Add(new(7,3,1,0));
        var f=new ShopFixture(s=>{
            var creature=s.Last(x=>x.ServiceType==typeof(Dfo.Server.Abstractions.Creatures.ICreatureRuntimeDomainService));
            s.AddSingleton<Dfo.Server.Abstractions.Creatures.ICreatureRuntimeDomainService>(p=>{
                var proxy=DispatchProxy.Create<Dfo.Server.Abstractions.Creatures.ICreatureRuntimeDomainService,CreatureProbe>();var spy=(CreatureProbe)(object)proxy;
                spy.Actual=creature.ImplementationInstance??creature.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,creature.ImplementationType!);spy.Trace=trace;return proxy;});
            s.AddSingleton<ILoggerProvider>(new LogProbe(trace));s.AddSingleton<IQuestContentRuleService>(rules);s.AddSingleton<ISkillRuleDefinitionSource>(learning);s.AddSingleton<ICharacterStatDefinitionSource>(new OrdinaryQuestEffectTests.CareerStats());
            s.AddSingleton<ICharacterSkillPointDomainService,CharacterSkillPointDomainService>();s.AddSingleton<ISelectedCharacterCurrentViewDomainService,PublishedView>();
            if(delivery is not null)s.AddSingleton<IOutboundSender>(delivery.Recorder);
            s.AddSingleton(p=>new GameplayOutboxes(p.GetRequiredService<IOutboundSender>(),p.GetRequiredService<GameplayCloseCauses>(),p.GetRequiredService<ILogger<GameplayOutbox>>()));
            if(protocol==3)RemainingOpeningProtocolCommandFlowTests.DomainObserver.Add<IQuestCompletionSettlementDomainService>(s,trace.Domain);
            var name=Modern?Whole[protocol]:Fine[protocol];var descriptor=s.Last(x=>x.ServiceType.IsGenericType&&x.ServiceType.GetGenericTypeDefinition()==typeof(ICommandHandler<,>)&&x.ServiceType.GenericTypeArguments[0].Name==name);
            var wrapper=typeof(CommandProbe<,>).MakeGenericType(descriptor.ServiceType.GenericTypeArguments);s.AddScoped(descriptor.ServiceType,p=>Activator.CreateInstance(wrapper,ActivatorUtilities.CreateInstance(p,descriptor.ImplementationType!),trace,p,protocol)!);
            var units=s.Last(x=>x.ServiceType==typeof(ICommandWorkUnits));s.AddSingleton<ICommandWorkUnits>(p=>new UnitProbe((ICommandWorkUnits)(units.ImplementationInstance??units.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,units.ImplementationType!)),trace,p,protocol));
        });
        f.SetLevel(20);f.Sql("INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0); INSERT INTO character_pvp(character_id) VALUES(1); INSERT INTO character_settings(character_id) VALUES(1); INSERT INTO character_expert_job(character_id) VALUES(1)");
        if(protocol!=0)f.Sql($"INSERT INTO character_active_quests VALUES(1,0,2001,{(protocol==2?1:0)})");
        await f.Seed(3,2000,1);return f;
    }
    private static ushort WireType(int protocol)=>(ushort)(0x1F+protocol);
    private static ushort[] ExpectedFrames(int p)=>p switch {0=>[0x1F,0x15],1=>[0x20,0x23F,0x15],2=>[0x21],_=>[0x22,0x25,0x15]};
    private static ValueTask Send(ShopFixture f,int p)=>EffectRequestTransport.SendRaw(f,new(1,WireType(p),0,7),p switch {0=>[0x1F,0,0xD1,7],1=>[0x20,0,0xD1,7],2=>[0x21,0,0xD1,7,0,0],_=>[0x22,0,0xD1,7,0xFF,0xFF,1,0,0xFF,0xFF]});
    private static string Assets(ShopFixture f)=>string.Join('|',f.Scalar("SELECT exp FROM characters WHERE character_id=1"),f.Count(2000),f.Scalar("SELECT count(*) FROM character_active_quests WHERE character_id=1"),f.Scalar("SELECT count(*) FROM character_invisible_flags WHERE character_id=1"));
    private static ushort Type(byte[] frame)=>BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
    private static void AssertChange(ShopFixture f,int p){Assert.Equal(p==0||p==2?1:0,f.Scalar("SELECT count(*) FROM character_active_quests WHERE character_id=1"));Assert.Equal(p==0?3:p==3?4:1,f.Count(2000));if(p==2)Assert.Equal(0,f.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=1"));if(p==3){Assert.Equal(1,f.Scalar("SELECT flag_value FROM character_invisible_flags WHERE character_id=1 AND slot_index=2001"));Assert.Equal((int)f.ExperienceForLevel(20)+101,f.Scalar("SELECT exp FROM characters WHERE character_id=1"));}}
    private static void AssertDeclaration(ShopFixture f,Trace trace,int p){if(!Modern)return;var d=Assert.Single(trace.Units);Assert.Equal(p==3?CommandWorkUnitKind.MainSettlement:CommandWorkUnitKind.Ordinary,d.Kind);var actor=Assert.Single(d.Participants);Assert.Equal(f.Session,actor.Session);Assert.Same(f.Connection,actor.Connection);if(p!=3)Assert.Equal(CommandFootprintMode.None,d.Footprint);}
    private static EffectOperationEntry Entry(ShopFixture f) {
        var table=typeof(EffectOperationLedger).GetField("ledgers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Services.GetRequiredService<EffectOperationLedger>())!;
        object?[] args=[f.Connection,null]; Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,args)!);
        return Assert.Single(((IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!).Values.Cast<EffectOperationEntry>());
    }
    private static void CloseChain(ShopFixture f, Trace trace) => CloseChain(f,trace.Chain);
    private static void CloseChain(ShopFixture f, GameplayCallChain expected) {
        Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var close));
        Assert.Equal(expected.RootId,close.RootId); Assert.Equal(GameplayCallSource.Connection,close.Source); Assert.Equal(f.Connection.ConnectionId,close.SourceConnection);
    }
    private static void ClosedMainRequest(ShopFixture f, Trace trace, int protocol) {
        if (!Modern) { Assert.Null(trace.Context!.Request); return; }
        var request=Assert.IsAssignableFrom<CommandRequestContext>(trace.Context!.Request); Assert.True(request.IsClosed);
        if(protocol==3) { var selected=Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(request); Assert.Same(f.Connection,selected.Dispatch.Connection); Assert.Equal(f.Session,selected.Recipient.Session); }
        else { var plain=Assert.IsType<CommandRequestContext.ProtocolNonEffect>(request);Assert.Same(f.Connection,plain.Dispatch.Connection);var metadata=Assert.IsType<ProtocolFrameMetadata.Game>(plain.Metadata).Value;Assert.Equal(WireType(protocol),metadata.Type);Assert.Equal(1,metadata.InboundOrdinal); }
    }
    public sealed class Trace {
        internal RemainingOpeningProtocolCommandFlowTests.Trace Domain = new(); internal Exception? ScopeFailure; internal GameplayCallChain Chain; internal CommandExecutionContext? Context; internal List<CommandWorkUnitDeclaration> Units=[]; internal List<string> Errors=[]; internal List<SelectedSessionIdentity> Discarded=[];
    }
    public sealed class CommandProbe<TCommand,TResult>(object actual,Trace trace,IServiceProvider p,int protocol):ICommandHandler<TCommand,TResult>,IDisposable where TCommand:class,ICommand<TResult> where TResult:notnull {
        public ValueTask<TResult> HandleAsync(CommandExecutionContext c,TCommand command,CancellationToken ct) { trace.Context=c;trace.Chain=GameplayJob.Current!.Value; if(!Modern && protocol!=3) trace.Domain.Attempts.Add(new((DomainTransaction)p.GetRequiredService<IDomainTransactionAccessor>().Current!)); return ((ICommandHandler<TCommand,TResult>)actual).HandleAsync(c,command,ct); }
        public void Dispose() { if(trace.ScopeFailure is {} cause) throw cause; }
    }
    private sealed class UnitProbe(ICommandWorkUnits actual,Trace trace,IServiceProvider p,int protocol):ICommandWorkUnits {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken t=default) where T:notnull {trace.Units.Add(d);return actual.WriteAsync(c,d,(u,ct)=> { if(protocol!=3) trace.Domain.Attempts.Add(new((DomainTransaction)p.GetRequiredService<IDomainTransactionAccessor>().Current!)); return a(u,ct); },t);}
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken t=default) where T:notnull {trace.Units.Add(d);return actual.WriteResultAsync(c,d,a,t);}
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken t=default) where T:notnull => actual.ReadOnlyAsync(c,d,a,t);
    }
    private sealed class PublishedView(ICharacterRosterDomainService characters,ICharacterEntryStateDomainService entries,ICharacterInventoryEntryDomainService inventory,ICharacterSkillPointDomainService points):ISelectedCharacterCurrentViewDomainService {
        public async ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity s,CancellationToken ct) {
            var entry=await entries.LoadAsync(s.AccountId,s.CharacterId,ct);
            entry=entry with {Skills=entry.Skills with {ProtocolMirror=await points.RemainingAsync(s.CharacterId,ct)}};
            return new(await characters.LoadForSelectionAsync(s.AccountId,0,ct),entry,
                await inventory.ReadCurrentAsync(s.AccountId,s.CharacterId,ct),new(s.AccountId,new(s.AccountId,new(InventoryContainerType.AccountCargo,0,[]),[],0,0),[]),null!,new GameWorldEntryState(DateTimeOffset.UnixEpoch,StubWorldContentEntryRuleService.EmptyContent,new ChannelEntryPolicyState(11,null!,true,false)),null!);
        }
    }
    public class CreatureProbe:DispatchProxy
    {
        internal object Actual=null!;internal Trace Trace=null!;
        protected override object? Invoke(MethodInfo? method,object?[]? args){
            if(method!.Name=="DiscardExact")Trace.Discarded.Add((SelectedSessionIdentity)args![0]!);
            try{return method.Invoke(Actual,args);}catch(TargetInvocationException error)when(error.InnerException is {} cause){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw();throw;}
        }
    }
    private sealed class LogProbe(Trace trace):ILoggerProvider,ILogger { public ILogger CreateLogger(string category)=>this;public void Dispose(){}public IDisposable? BeginScope<T>(T state) where T:notnull=>null;public bool IsEnabled(LogLevel level)=>true;public void Log<T>(LogLevel level,EventId id,T state,Exception? e,Func<T,Exception?,string> formatter) {if(e is not null) trace.Errors.Add(formatter(state,e)+e);}}
    private static IEnumerable<byte[]> Split(IEnumerable<byte[]> sends) { foreach(var bytes in sends) for(var offset=0;offset<bytes.Length;) { var length=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset+3));yield return bytes[offset..(offset+length)];offset+=length; } }
}
