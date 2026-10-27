using Dfo.Server.Application.Tests.Support;
using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class DeleteItemProtocolCommandFlowTests(ITestOutputHelper output)
{
    // These tests are installed and run against the original registered decoder/handler before migration.
    // They observe assets and wire output; the same tests survive the command boundary change.
    [Theory]
    [InlineData(1,65,5000,10,3,0u,0)]
    [InlineData(1,65,5000,10,3,91u,1)]
    [InlineData(1,65,5000,1,1,0u,0)]
    [InlineData(2,65,5000,3,2,0u,1)]
    [InlineData(2,358,3037,2,1,0u,1)]
    [InlineData(3,359,3262,2,1,0u,1)]
    [InlineData(3,359,3262,1,1,0u,1)]
    public async Task Real_three_operations_preserve_complete_assets_events_and_one_ordered_batch(
        ushort operation,int slot,int item,int seed,int count,uint reserved,byte safe)
    {
        var trace=new Trace();using var f=Fixture(trace);if(slot>=356)await f.SeedShared(item,seed);else await f.Seed(slot,item,seed);
        if(operation==1)f.Sql("INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) VALUES(1,0,2001,0)");
        var before=await Assets(f);await DeleteWire.Send(f,slot,item,count,operation,reserved:reserved,safe:safe,trace:trace);
        var after=await Assets(f);Write("success",new{operation,slot,item,seed,count,reserved,safe,before,after,attempts=Attempts(trace),projections=trace.Projections,events=trace.Events,inputs=trace.Inputs,frames=Frames(f)});
        Assert.Equal(seed-count,slot>=356?f.SharedCount(item):f.Count(item));var a=Assert.Single(trace.Attempts);Assert.True(a.Completed);Assert.Equal(DomainTransactionCompletion.Committed,a.Tx.Completion);
        if(operation==1){Assert.Same(a.Tx,trace.ClockTx);Assert.Equal(slot<356?1:0,trace.QuestCalls);if(seed==1)Assert.Equal(1,f.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=2001"));}
        else{var ev=Assert.Single(trace.Events);Assert.Same(a.Tx,ev.Tx);Assert.Equal(count,ev.Payload.Count);Assert.Null(trace.ProjectionTransaction);Assert.InRange(trace.PreparedAt,trace.ProjectionStart,trace.ProjectionEnd);}
        var batch=Assert.Single(f.Sender.Batches);Assert.Same(f.Connection,batch.Connection);var ack=Assert.IsType<DeleteItemResponse>(f.Sender.Messages[0]);Assert.True(ack.Success);Assert.Equal(count,ack.AppliedCount);Assert.Equal(new GameSlot(InventoryContainerType.Main,slot),ack.Slot);
        Assert.Equal(operation==1?(seed==count?2:1):slot>=356&&seed==count?1:2,batch.Frames.Length);Assert.Equal((ushort)0x0012,Type(batch.Frames[0]));AssertNoLedger(f);
    }
    [Fact]
    public async Task Real_equipment_deletion_uses_one_UID_not_InstanceValue_and_keeps_full_core()
    {
        var trace=new Trace();using var f=Fixture(trace);await f.SeedCore(9,StackableItemCore.Create(1,3000,27312)with{Durability=41,EnchantCardId=42});var before=await Assets(f);var uid=f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=9");
        await DeleteWire.Send(f,9,3000,1,trace:trace);Assert.Equal(0,f.Count(3000));Assert.Equal(1,Assert.IsType<DeleteItemResponse>(Assert.Single(f.Sender.Messages)).AppliedCount);Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Attempts).Tx.Completion);
        Write("equipment",new{uid,before,after=await Assets(f),attempts=Attempts(trace),inputs=trace.Inputs,frames=Frames(f)});
    }
    [Theory]
    [InlineData(1,false)] [InlineData(1,true)] [InlineData(2,false)] [InlineData(2,true)]
    public async Task SQL_action_and_actual_native_Commit_keep_cause_atomic_quest_or_material_and_retry(ushort operation,bool native)
    {
        var trace=new Trace{Native=native,ObserveSql=true};using var f=Fixture(trace);await f.Seed(65,5000,1);f.Sql("INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) VALUES(1,0,2001,0)");var before=await Assets(f);
        var ddl=native?"CREATE TABLE delete_parent(id INTEGER PRIMARY KEY);CREATE TABLE delete_child(id INTEGER REFERENCES delete_parent(id) DEFERRABLE INITIALLY DEFERRED);CREATE TRIGGER delete_fault AFTER DELETE ON item_slots WHEN OLD.owner_scope='character' AND OLD.owner_id=1 AND OLD.list_type=0 AND OLD.slot_index=65 BEGIN INSERT INTO delete_child VALUES(404);END;":"CREATE TRIGGER delete_fault BEFORE DELETE ON item_slots WHEN OLD.owner_scope='character' AND OLD.owner_id=1 AND OLD.list_type=0 AND OLD.slot_index=65 BEGIN SELECT RAISE(ABORT,'delete action SQL failure');END;";
        f.Sql(ddl);var error=await Record.ExceptionAsync(()=>DeleteWire.Send(f,65,5000,1,operation,sequence:7,trace:trace).AsTask());var cause=Assert.IsType<SqliteException>(error);Assert.Equal(19,cause.SqliteErrorCode);Assert.Equal(native?787:1811,cause.SqliteExtendedErrorCode);
        var a=Assert.Single(trace.Attempts);Assert.Equal(native,a.Completed);Assert.Equal(1,a.ForeignKeys);Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,a.Tx.Completion);Assert.Equal(before,await Assets(f));
        if(native){Assert.True(a.Children>0);Assert.Equal(0,a.StagedCount);Assert.Same(cause,a.Tx.CommitFailure!.Cause);SqliteFailureAssertions.FromCommit(a.Tx, cause);if(operation==1){Assert.Equal(1,trace.QuestCalls);Assert.Equal(1,trace.StagedQuest);Assert.Same(a.Tx,trace.QuestTx);}else{Assert.Same(a.Tx,Assert.Single(trace.Events).Tx);Assert.Contains("character.material-consumed",a.Stage!,StringComparison.Ordinal);}}
        else{Assert.Same(cause,a.Failure);SqliteFailureAssertions.FromAction(a.Tx, cause);}
        Assert.Empty(f.Sender.Batches);var close=Assert.Single(f.Sender.Outbound.Entries);Assert.True(close.IsClose);Assert.Same(f.Connection,close.Connection);Assert.True(GameplayJob.EndedAJob(cause));Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));AssertNoLedger(f);
        var fault=new{operation,native,ddl,before,after=await Assets(f),attempts=Attempts(trace),cause=cause.ToString(),causeSame=ReferenceEquals(cause,native?a.Tx.CommitFailure!.Cause:a.Failure),sourceCloseExact=ReferenceEquals(f.Connection,close.Connection),inputs=trace.Inputs.ToArray()};
        f.Sql("DROP TRIGGER delete_fault");await DeleteWire.Send(f,65,5000,1,operation,sequence:7,trace:trace);Assert.Equal(0,f.Count(5000));Assert.Single(f.Sender.Batches);Assert.False(trace.Inputs.Last().SequenceIsFresh);Write("sql",new{fault,afterRetry=await Assets(f),inputs=trace.Inputs,frames=Frames(f)});
    }
    [Theory]
    [InlineData(1,false)] [InlineData(1,true)] [InlineData(3,false)] [InlineData(3,true)]
    public async Task Ordinary_projection_encoder_or_composer_failure_remains_isolated_Warning_Failed(ushort operation,bool composer)
    {
        var trace=new Trace{ComposerFault=composer};using var f=Fixture(trace);await f.Seed(65,5000,3);int calls=0;if(!composer)f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);calls++;return trace.Cause;});
        var error=await Record.ExceptionAsync(()=>DeleteWire.Send(f,65,5000,1,operation,trace:trace).AsTask());Assert.Null(error);Assert.Equal(2,f.Count(5000));Assert.Empty(f.Sender.Batches);Assert.Empty(f.Sender.Outbound.Closes);Assert.Single(trace.Logs,x=>x.Level==LogLevel.Warning&&ReferenceEquals(x.Error,trace.Cause));Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Attempts).Tx.Completion);AssertNoLedger(f);
        Write("projection",new{operation,composer,calls,assets=await Assets(f),attempts=Attempts(trace),inputs=trace.Inputs,cause=trace.Cause.ToString(),warningSame=trace.Logs.Any(x=>x.Level==LogLevel.Warning&&ReferenceEquals(x.Error,trace.Cause)),frames=Frames(f)});
    }
    [Theory]
    [InlineData(1,"pre")] [InlineData(1,"normal")] [InlineData(2,"pre")] [InlineData(2,"normal")]
    public async Task Direct_ACK_encoder_fault_distinguishes_precondition_and_committed_normal_refusal(ushort operation,string branch)
    {
        var trace=new Trace();using var f=Fixture(trace);await f.Seed(65,5000,3);var before=await Assets(f);int calls=0;
        f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);trace.Chain=GameplayJob.Current;calls++;return trace.Cause;});
        var error=await Record.ExceptionAsync(()=>DeleteWire.Send(f,branch=="pre"?352:65,5000,branch=="normal"?4:1,operation,trace:trace).AsTask());
        // The original legacy route yields raw cause. The migrated default wraps only a committed unit.
        if(branch=="normal"&&trace.Contexts.Count>0){var typed=Assert.IsType<TransactionOutcomeUncertainException>(error);Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure,typed.Reason);Assert.Same(trace.Cause,typed.InnerException);var request=Assert.IsType<CommandRequestContext.ProtocolNonEffect>(trace.Contexts.Single().Request);Assert.True(request.IsClosed);Assert.True(request.HasCommittedUnit);Assert.Empty(request.CommittedParticipants);Assert.Empty((System.Collections.ICollection)typeof(CommandRequestContext).GetField("attempts",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(request)!);}
        else Assert.Same(trace.Cause,error);
        Assert.True(GameplayJob.EndedAJob(error!));Assert.Equal(before,await Assets(f));Assert.Equal(1,calls);Assert.Empty(f.Sender.Messages);Assert.Empty(f.Sender.Batches);var close=Assert.Single(f.Sender.Outbound.Entries);Assert.True(close.IsClose);Assert.Same(f.Connection,close.Connection);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));AssertNoLedger(f);
        if(branch=="pre")Assert.Empty(trace.Attempts);else Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Attempts).Tx.Completion);
        var chain=trace.Chain!.Value;Assert.True(chain.IsInitialized);Assert.Equal(0,chain.Depth);Assert.Null(chain.ParentId);Assert.Equal(GameplayCallSource.Connection,chain.Source);Assert.Equal(f.Connection.ConnectionId,chain.SourceConnection);var log=Assert.Single(trace.Logs,x=>x.Level==LogLevel.Error&&ReferenceEquals(x.Error,error));Assert.Equal(chain.RootId,log.Fields["RootId"]);Assert.Equal(chain.ParentId,log.Fields["ParentId"]);Assert.Equal(chain.Depth,log.Fields["Depth"]);Assert.Equal(chain.Source,log.Fields["Source"]);Assert.Equal(chain.SourceConnection,log.Fields["SourceConnection"]);Assert.Equal(chain.CommandType,log.Fields["CommandType"]);
        Write("refusal",new{operation,branch,before,after=await Assets(f),cause=error!.ToString(),causeSame=ReferenceEquals(error is TransactionOutcomeUncertainException ex?ex.InnerException:error,trace.Cause),typed=TransactionOutcomeUncertainException.Contains(error),reason=(error as TransactionOutcomeUncertainException)?.Reason.ToString(),chain,attempts=Attempts(trace),calls,inputs=trace.Inputs,sourceCloseExact=ReferenceEquals(f.Connection,close.Connection),endedAJob=GameplayJob.EndedAJob(error),frames=Frames(f)});
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Real_old_session_query_preserves_op1_refusal_and_material_silence(ushort operation)
    {
        var trace=new Trace();using var f=Fixture(trace);await f.Seed(65,5000,3);var old=f.Session;f.Select(2);trace.Stale=old;var before=await Assets(f);await DeleteWire.Send(f,65,5000,1,operation,trace:trace);Assert.Equal(before,await Assets(f));if(operation==1)Assert.False(Assert.IsType<DeleteItemResponse>(Assert.Single(f.Sender.Messages)).Success);else Assert.Empty(f.Sender.Messages);Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Attempts).Tx.Completion);Assert.Empty(f.Sender.Outbound.Closes);Write("stale",new{operation,before,after=await Assets(f),attempts=Attempts(trace),inputs=trace.Inputs,frames=Frames(f),scope="read-only selected query returns real previous session; IsInWorld uses actual registry; wire carries no old-session field"});
    }
    [Theory]
    [InlineData(1)] [InlineData(3)]
    public async Task Same_raw_sequence_debits_twice_then_current_insufficiency_is_not_replayed(ushort operation)
    {
        var trace=new Trace();using var f=Fixture(trace);await f.Seed(65,5000,2);await DeleteWire.Send(f,65,5000,1,operation,sequence:7,trace:trace);Assert.Equal(1,f.Count(5000));await DeleteWire.Send(f,65,5000,1,operation,sequence:7,trace:trace);Assert.Equal(0,f.Count(5000));await DeleteWire.Send(f,65,5000,1,operation,sequence:7,trace:trace);var acks=f.Sender.Messages.OfType<DeleteItemResponse>().ToArray();Assert.Equal(new[]{true,true,false},acks.Select(x=>x.Success));Assert.Equal(new[]{true,false,false},trace.Inputs.Select(x=>x.SequenceIsFresh));AssertNoLedger(f);Write("sameSeq",new{operation,assets=await Assets(f),attempts=Attempts(trace),inputs=trace.Inputs,frames=Frames(f)});
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Shared_seed_two_request_one_has_exact_asset_and_material_event_count(ushort operation)
    {
        var trace=new Trace();using var f=Fixture(trace);if(operation==1)await f.Seed(65,5000,2);else await f.SeedShared(3037,2);var before=await Assets(f);await DeleteWire.Send(f,operation==1?65:358,operation==1?5000:3037,1,operation,trace:trace);var after=await Assets(f);Write("mutationAsset",new{operation,before,after,actualCount=operation==1?f.Count(5000):f.SharedCount(3037),events=trace.Events,inputs=trace.Inputs,frames=Frames(f)});Assert.Equal(1,operation==1?f.Count(5000):f.SharedCount(3037));if(operation!=1)Assert.Equal(1,Assert.Single(trace.Events).Payload.Count);Assert.True(Assert.IsType<DeleteItemResponse>(f.Sender.Messages[0]).Success);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Beyond_wire_batch_prevalidation_and_late_carried_SQL_restore_shared_and_events(bool effect)
    {
        var trace=new Trace();using var f=Fixture(trace);await f.SeedShared(3037,3);await f.Seed(65,5000,3);var before=await Assets(f);
        MaterialCost cube=new(new(BusinessInventorySpace.SharedMaterial,4),3037,2);MaterialCost carried=new(new(BusinessInventorySpace.Consumable,0),5000,4);
        var rejected=await DirectMaterials(f,f.Session,[cube,carried]);Assert.False(rejected.Success);Assert.Equal(before,await Assets(f));
        f.Sql("CREATE TRIGGER batch_fault BEFORE DELETE ON item_slots WHEN OLD.owner_scope='character' AND OLD.slot_index=65 BEGIN SELECT RAISE(ABORT,'late carried batch failure');END;");
        trace.Capturing=true;var error=await Record.ExceptionAsync(()=>DirectMaterials(f,f.Session,[cube,carried with{Count=3}]).AsTask());trace.Capturing=false;var cause=Assert.IsType<SqliteException>(error);Assert.Equal(1811,cause.SqliteExtendedErrorCode);Assert.Same(cause,trace.Attempts.Last().Failure);Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,trace.Attempts.Last().Tx.Completion);Assert.Equal(2,Assert.Single(trace.Events).Payload.Count);Assert.Equal(before,await Assets(f));Assert.Empty(f.Sender.Messages);
        Write("batch",new{effect,before,after=await Assets(f),attempts=Attempts(trace),events=trace.Events,cause=cause.ToString(),scope="original multi-cost domain API with explicit original transaction; not a multi-cost wire"});
    }
    internal static async ValueTask<MaterialConsumptionResult> DirectMaterials(ShopFixture f,SelectedSessionIdentity session,IReadOnlyList<MaterialCost> costs)=>await TestGameplay.RunAsync(f.Services,async()=>{await using var tx=f.Services.GetRequiredService<DomainTransactionScope>().Begin();var result=await f.Services.GetRequiredService<IMaterialConsumptionDomainService>().ConsumeAsync(session,costs,default);await tx.CommitAsync(default);return result;});
    private void Write(string kind,object data)=>output.WriteLine(JsonSerializer.Serialize(new{task23=true,kind,data}));
    private static object[] Attempts(Trace t)=>t.Attempts.Select(a=>(object)new{completion=a.Tx.Completion.ToString(),a.Completed,failure=a.Failure?.ToString(),a.ForeignKeys,a.Children,a.StagedCount,a.Stage}).ToArray();
    private static string[] Frames(ShopFixture f)=>f.Sender.Outbound.Frames.Select(Convert.ToHexString).ToArray();
    private static ShopFixture Fixture(Trace trace)=>new(services=>{
        services.AddSingleton(trace);services.AddSingleton<IQuestContentRuleService,HeldItemQuestRules>();
        services.AddSingleton<ItemDiscardDomainService>();services.AddSingleton<IItemDiscardDomainService>(p=>new ObserveDiscard(p.GetRequiredService<ItemDiscardDomainService>(),p,trace));services.AddSingleton<MaterialConsumptionDomainService>();services.AddSingleton<IMaterialConsumptionDomainService>(p=>new ObserveMaterials(p.GetRequiredService<MaterialConsumptionDomainService>(),p,trace));
        var quest=services.Last(x=>x.ServiceType==typeof(IQuestProgressDomainService));services.AddSingleton<IQuestProgressDomainService>(p=>new ObserveQuest(Resolve<IQuestProgressDomainService>(quest,p),p,trace));
        var ev=services.Last(x=>x.ServiceType==typeof(IEventService));services.AddSingleton<ObserveEvents>(p=>new(Resolve<IEventService>(ev,p),p,trace));services.AddSingleton<IEventService>(p=>p.GetRequiredService<ObserveEvents>());services.AddSingleton<ICommandProjections>(p=>p.GetRequiredService<ObserveEvents>());
        var clock=(TimeProvider)services.Last(x=>x.ServiceType==typeof(TimeProvider)).ImplementationInstance!;services.AddSingleton<TimeProvider>(p=>new ObserveClock(clock,p,trace));var selected=services.Last(x=>x.ServiceType==typeof(ISelectedSessionDomainService));services.AddSingleton<ISelectedSessionDomainService>(p=>QueryProxy.Create(Resolve<ISelectedSessionDomainService>(selected,p),trace));services.AddScoped<CommandWorkUnits>();services.AddScoped<ICommandWorkUnits>(p=>new ObserveUnits(p.GetRequiredService<CommandWorkUnits>(),trace));services.AddLogging(log=>log.AddProvider(new Logs(trace)));
    });
    private static T Resolve<T>(ServiceDescriptor d,IServiceProvider p)where T:class=>(T)(d.ImplementationInstance??d.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,d.ImplementationType!));
    internal sealed class Trace
    {
        internal readonly List<DeleteWireObservation> Inputs=[];internal readonly List<Attempt> Attempts=[];internal readonly List<LogRow> Logs=[];internal readonly List<CommandExecutionContext> Contexts=[];internal readonly List<MaterialEvent> Events=[];internal readonly List<string> Projections=[];
        internal GameplayCallChain? Chain;internal bool Native,ObserveSql,ComposerFault,Capturing,AwaitClock;internal DomainTransaction? ClockTx,QuestTx;internal DateTimeOffset ClockValue,ProjectionStart,ProjectionEnd,PreparedAt;internal IDomainTransaction? ProjectionTransaction;internal int QuestCalls,StagedQuest;internal SelectedSessionIdentity? Stale;internal readonly IOException Cause=new("fixed delete actual encoder or composer cause");
    }
    internal sealed record Attempt(DomainTransaction Tx){internal bool Completed;internal Exception? Failure;internal string? Stage;internal int ForeignKeys,Children,StagedCount;}
    internal sealed record MaterialEvent(DomainTransaction Tx,MaterialConsumed Payload);
    private static Attempt Begin(IServiceProvider p,Trace t){var tx=Assert.IsType<DomainTransaction>(p.GetRequiredService<IDomainTransactionAccessor>().Current);Assert.False(tx.IsReadOnly);Assert.False(tx.IsSettled);var a=new Attempt(tx);t.Attempts.Add(a);return a;}
    private static async ValueTask Stage(IServiceProvider p,Trace t,Attempt a,CancellationToken ct){a.ForeignKeys=await Scalar(p,"PRAGMA foreign_keys",ct);a.Children=t.Native?await Scalar(p,"SELECT count(*) FROM delete_child",ct):0;a.StagedCount=await Scalar(p,"SELECT COALESCE(SUM(count),0) FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=65",ct);a.Stage=await AssetsIn(p,ct);}
    private sealed class ObserveDiscard(IItemDiscardDomainService actual,IServiceProvider p,Trace t):IItemDiscardDomainService
    {public async ValueTask<ItemDiscardResult> DiscardAsync(SelectedSessionIdentity session,BusinessSlot slot,byte entries,ushort op,int id,int count,DateTimeOffset now,CancellationToken ct){var a=Begin(p,t);Assert.Same(a.Tx,t.ClockTx);Assert.Equal(t.ClockValue,now);ItemDiscardResult? r=null;try{r=await actual.DiscardAsync(session,slot,entries,op,id,count,now,ct);a.Completed=true;return r;}catch(Exception e){a.Failure=e;throw;}finally{if(a.Failure!=null||r?.Receipt!=null)await Stage(p,t,a,ct);}}}
    // Do not read SQL on a normal refusal: it would enlist a participant the original did not touch.
    private sealed class ObserveMaterials(IMaterialConsumptionDomainService actual,IServiceProvider p,Trace t):IMaterialConsumptionDomainService
    {public async ValueTask<MaterialConsumptionResult> ConsumeAsync(SelectedSessionIdentity session,IReadOnlyList<MaterialCost> costs,CancellationToken ct){var a=Begin(p,t);MaterialConsumptionResult? r=null;try{r=await actual.ConsumeAsync(session,costs,ct);a.Completed=true;return r;}catch(Exception e){a.Failure=e;throw;}finally{if(a.Failure!=null||r?.Success==true)await Stage(p,t,a,ct);}}}
    private sealed class ObserveQuest(IQuestProgressDomainService actual,IServiceProvider p,Trace t):IQuestProgressDomainService
    {public async ValueTask<QuestProgressReport> ReportAsync(AccountId account,CharacterId character,IReadOnlyList<QuestProgressFact> facts,CancellationToken ct){if(t.Capturing){Assert.IsType<QuestHoldingsChanged>(Assert.Single(facts));t.QuestCalls++;t.QuestTx=Assert.IsType<DomainTransaction>(p.GetRequiredService<IDomainTransactionAccessor>().Current);Assert.Same(t.Attempts.Last().Tx,t.QuestTx);}var r=await actual.ReportAsync(account,character,facts,ct);if(t.Capturing){await Stage(p,t,t.Attempts.Last(),ct);t.StagedQuest=checked((int)(r.NotificationSnapshot?.Quests.Single().TriggerValue??0));}return r;}}
    private sealed class ObserveEvents(IEventService actual,IServiceProvider p,Trace t):IEventService,ICommandProjections
    {
        public async ValueTask<Guid> EnqueueAsync<T>(T payload,CancellationToken ct=default)where T:class,IDomainEvent{if(t.Capturing&&payload is MaterialConsumed ev){var tx=Assert.IsType<DomainTransaction>(p.GetRequiredService<IDomainTransactionAccessor>().Current);Assert.Same(t.Attempts.Last().Tx,tx);t.Events.Add(new(tx,ev));}return await actual.EnqueueAsync(payload,ct);}
        public ValueTask DirectAsync<T>(T payload,CancellationToken ct=default)where T:class,IDomainEvent=>actual.DirectAsync(payload,ct);
        public async ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience,T notification,CancellationToken ct=default)where T:class,INotification<T>{if(t.Capturing){t.Projections.Add(typeof(T).Name);t.ProjectionTransaction=p.GetRequiredService<IDomainTransactionAccessor>().Current;if(notification is MaterialsConsumed m){t.PreparedAt=m.PreparedAt;t.ProjectionStart=t.Inputs.Last().Started;t.ProjectionEnd=DateTimeOffset.UtcNow;}}return await actual.ProjectAsync(audience,notification,ct);}
    }
    private sealed class ObserveClock(TimeProvider actual,IServiceProvider p,Trace t):TimeProvider
    {public override DateTimeOffset GetUtcNow(){var now=actual.GetUtcNow();if(t.Capturing&&t.AwaitClock&&p.GetRequiredService<IDomainTransactionAccessor>().Current is DomainTransaction tx){t.ClockTx=tx;t.ClockValue=now;t.AwaitClock=false;}return now;}}
    public class QueryProxy:DispatchProxy
    {private ISelectedSessionDomainService actual=null!;private Trace trace=null!;internal static ISelectedSessionDomainService Create(ISelectedSessionDomainService actual,Trace trace){var x=Create<ISelectedSessionDomainService,QueryProxy>();((QueryProxy)x).actual=actual;((QueryProxy)x).trace=trace;return x;}protected override object? Invoke(MethodInfo? method,object?[]? args){if(method!.Name==nameof(ISelectedSessionDomainService.FindByConnection)&&trace.Stale is{} old)return old;if(method.Name==nameof(ISelectedSessionDomainService.IsInWorld)&&trace.ComposerFault&&trace.Attempts.LastOrDefault()?.Tx.Completion==DomainTransactionCompletion.Committed)throw trace.Cause;try{return method.Invoke(actual,args);}catch(TargetInvocationException e)when(e.InnerException is{} cause){ExceptionDispatchInfo.Throw(cause);throw;}}}
    private sealed class ObserveUnits(ICommandWorkUnits actual,Trace trace):ICommandWorkUnits
    {public ValueTask<T> WriteAsync<T>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> action,CancellationToken ct=default)where T:notnull{var req=Assert.IsType<CommandRequestContext.ProtocolNonEffect>(context.Request);Assert.Empty(declaration.Participants);Assert.Equal(CommandWorkUnitKind.Ordinary,declaration.Kind);Assert.Equal(CommandFootprintMode.None,declaration.Footprint);Assert.Same(((DeleteItemRequest)req.Message).Metadata,Assert.IsType<ProtocolFrameMetadata.Game>(req.Metadata).Value);trace.Contexts.Add(context);return actual.WriteAsync(context,declaration,action,ct);}public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new InvalidOperationException("DeleteItem ordinary Write has no commit-result recovery.");public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new InvalidOperationException("DeleteItem keeps its writable transaction.");}
    internal sealed record LogRow(LogLevel Level,Exception? Error,string Message,IReadOnlyDictionary<string,object?> Fields);
    private sealed class Logs(Trace t):ILoggerProvider{public ILogger CreateLogger(string name)=>new Logger(t);public void Dispose(){}private sealed class Logger(Trace t):ILogger{public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;public bool IsEnabled(LogLevel l)=>true;public void Log<TState>(LogLevel l,EventId id,TState state,Exception? error,Func<TState,Exception?,string> format)=>t.Logs.Add(new(l,error,format(state,error),state is IEnumerable<KeyValuePair<string,object?>> fields?fields.ToDictionary(x=>x.Key,x=>x.Value):new Dictionary<string,object?>()));}}
    private sealed class HeldItemQuestRules:IQuestContentRuleService
    {private static readonly QuestDefinition Quest=new(2001,"hold an item",null,null,null,null,null,null,[],"seeking","",null,null,null,null,null,null,null,[],[],[],[],SeekingItems:[new SeekingItemRequirement(5000,1)]);public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken ct)=>ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([Quest]);public ValueTask<QuestDefinition?> FindAsync(int id,CancellationToken ct)=>ValueTask.FromResult<QuestDefinition?>(id==2001?Quest:null);public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken ct)=>ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);public QuestParameters Parameters()=>throw new NotSupportedException();}
    private static ushort Type(byte[] frame)=>BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
    private static void AssertNoLedger(ShopFixture f){var table=typeof(Dfo.Server.Application.Effects.EffectOperationLedger).GetField("ledgers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Services.GetRequiredService<Dfo.Server.Application.Effects.EffectOperationLedger>())!;object?[] args=[f.Connection,null];Assert.False((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,args)!);}
    internal static Task<string> Assets(ShopFixture f)=>TestGameplay.RunAsync(f.Services,async()=>await AssetsIn(f.Services,default));
    private static async ValueTask<string> AssetsIn(IServiceProvider p,CancellationToken ct)
    {var tables=new[]{"items","item_slots","characters","character_stats","character_container_state","account_cargo_state","domain_events","domain_event_deliveries","character_active_quests","character_invisible_flags","character_quest_acceptance_times","character_quest_notify_selections"};var data=new SortedDictionary<string,string>();foreach(var table in tables)data[table]=await Rows(p,"SELECT * FROM "+table,ct);return JsonSerializer.Serialize(data);}
    private static ValueTask<string> Rows(IServiceProvider p,string sql,CancellationToken ct)=>p.GetRequiredService<SqliteDatabase>().ReadAsync(async(cmd,token)=>{cmd.CommandText=sql;await using var r=await cmd.ExecuteReaderAsync(token);var rows=new List<string>();while(await r.ReadAsync(token))rows.Add(JsonSerializer.Serialize(Enumerable.Range(0,r.FieldCount).ToDictionary(i=>r.GetName(i),i=>r.IsDBNull(i)?null:r.GetValue(i)is byte[] blob?Convert.ToHexString(blob):Convert.ToString(r.GetValue(i)))));rows.Sort(StringComparer.Ordinal);return "["+string.Join(",",rows)+"]";},ct);
    private static ValueTask<int> Scalar(IServiceProvider p,string sql,CancellationToken ct)=>p.GetRequiredService<SqliteDatabase>().ReadAsync(async(cmd,token)=>{cmd.CommandText=sql;return Convert.ToInt32(await cmd.ExecuteScalarAsync(token));},ct);
}
internal sealed record DeleteWireObservation(byte Selector,ushort Type,ushort Sequence,long? InboundOrdinal,bool SequenceIsFresh,int? FullBodyLength,string FullBodySha256,string Body,string CLR,int Logical,DateTimeOffset Started);
internal static class DeleteWire
{
    private sealed class State{internal ushort Sequence;internal readonly GameInboundFrameCounter Counter=new();}private static readonly ConditionalWeakTable<ShopFixture,State> states=new();
    internal static async ValueTask<IClientNetworkMessage> Send(ShopFixture f,int slot,int item,int count,ushort operation=1,byte entries=1,uint reserved=0,byte safe=1,InventoryContainerType container=InventoryContainerType.Main,ushort? sequence=null,DeleteItemProtocolCommandFlowTests.Trace? trace=null)
    {byte[] body=new byte[19];body[0]=(byte)container;body[1]=entries;BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2),operation);BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4),checked((ushort)slot));BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(6),item);BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(10),count);BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(14),reserved);body[18]=safe;var state=states.GetOrCreateValue(f);var seq=sequence??++state.Sequence;var(ordinal,fresh)=state.Counter.Observe(seq);var metadata=new GameMessageMetadata(1,0x0012,0,seq){InboundOrdinal=ordinal,SequenceIsFresh=fresh};metadata=metadata.WithRawBody(body);var reg=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1&&x.Type==0x0012);var decoded=Assert.IsType<DeleteItemRequest>(((IGameMessageDecoder)f.Services.GetRequiredService(reg.DecoderType)).Decode(metadata,body));Assert.Same(metadata,decoded.Metadata);Assert.Equal(19,metadata.FullBodyLength);Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)),metadata.FullBodySha256);trace?.Inputs.Add(new(metadata.Selector,metadata.Type,metadata.Sequence,metadata.InboundOrdinal,metadata.SequenceIsFresh,metadata.FullBodyLength,metadata.FullBodySha256!,Convert.ToHexString(body),decoded.GetType().FullName!,(int)decoded.MessageType,DateTimeOffset.UtcNow));if(trace!=null){trace.Capturing=true;trace.AwaitClock=true;}try{await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),decoded,default);return decoded;}finally{if(trace!=null)trace.Capturing=false;}}
}
