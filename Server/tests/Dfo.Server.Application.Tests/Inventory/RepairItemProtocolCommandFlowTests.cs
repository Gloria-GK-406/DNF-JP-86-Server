using Dfo.Server.Application.Tests.Support;
using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.RepairItem;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class RepairItemProtocolCommandFlowTests(ITestOutputHelper output)
{
    // Installed and run against the original registered decoder/handler before product migration.
    [Theory]
    [InlineData("single")] [InlineData("all")] [InlineData("personal-all")] [InlineData("auto")]
    public async Task Real_ordinary_and_automatic_wire_preserve_gold_core_usage_clock_and_ordered_batch(string mode)
    {
        var t=new Trace();using var f=Fixture(t);await Seed(f);if(mode!="single")await f.SeedCore(3,Piece(0));
        var before=await Assets(f);await RepairWire.Send(f,mode,trace:t);var after=await Assets(f);
        Assert.Equal(mode=="auto"?100000:mode=="single"?99887:99774,f.Gold);
        Assert.Equal((ushort)45,await f.Durability(InventoryContainerType.Equipment,12));
        Assert.Equal(mode=="auto"?1:0,Usage(f));Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(t.Attempts).Tx.Completion);
        Assert.Null(t.ProjectionTransaction);Assert.NotEmpty(t.ClockCalls);Assert.False(t.ClockCalls.Last().Open);Assert.Equal(f.Clock.GetUtcNow(),t.PreparedAt);
        if(mode=="auto"){Assert.Single(t.DirectFacts);Assert.Single(t.QuotaEvents);Assert.Contains(t.ClockCalls,x=>x.Open);}
        var batch=Assert.Single(f.Sender.Batches);Assert.Same(f.Connection,batch.Connection);Assert.Equal(mode=="auto"?2:1,batch.Frames.Length);
        var ack=Assert.IsType<RepairItemResponse>(f.Sender.Messages[0]);Assert.Equal(0,ack.ErrorCode);Assert.Equal(f.Gold,ack.Gold);
        if(mode=="personal-all")Assert.Equal(InventoryContainerType.PersonalCargo,ack.RepairAllSpace);
        var stored=f.StoredCore(12,InventoryContainerType.Equipment);Assert.Equal((byte)0xA0,stored[9]);Assert.Equal((byte)7,stored[17]);AssertNoLedger(f);
        Write("success",new{mode,before,after,attempts=Attempts(t),facts=t.DirectFacts,events=t.QuotaEvents,clocks=t.ClockCalls,inputs=t.Inputs,frames=Frames(f)});
    }

    [Theory] [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public async Task Actual_action_SQL_and_native_Commit_restore_gold_durability_and_direct_usage(bool automatic,bool native)
    {
        var t=new Trace{Native=native};using var f=Fixture(t);await Seed(f);var before=await Assets(f);
        var uid=f.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=3 AND slot_index=12");Assert.True(uid>0);
        // The trigger is enabled only after seed. Both faults use the actual same SQLite participant.
        var ddl=native?$"CREATE TABLE repair_parent(id INTEGER PRIMARY KEY);CREATE TABLE repair_child(id INTEGER REFERENCES repair_parent(id) DEFERRABLE INITIALLY DEFERRED);CREATE TRIGGER repair_fault AFTER UPDATE OF item_core ON items WHEN NEW.item_uid={uid} BEGIN INSERT INTO repair_child VALUES(404);END;":$"CREATE TRIGGER repair_fault BEFORE UPDATE OF item_core ON items WHEN NEW.item_uid={uid} BEGIN SELECT RAISE(ABORT,'repair action SQL failure');END;";
        f.Sql(ddl);var error=await Record.ExceptionAsync(()=>RepairWire.Send(f,automatic?"auto":"single",sequence:7,trace:t).AsTask());var cause=Assert.IsType<SqliteException>(error);
        Assert.Equal(19,cause.SqliteErrorCode);Assert.Equal(native?787:1811,cause.SqliteExtendedErrorCode);var a=Assert.Single(t.Attempts);
        Assert.Equal(native,a.Completed);Assert.Equal(1,a.ForeignKeys);Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,a.Tx.Completion);Assert.Equal(before,await Assets(f));
        if(native){Assert.True(a.Children>0);Assert.Same(cause,a.Tx.CommitFailure!.Cause);SqliteFailureAssertions.FromCommit(a.Tx, cause);Assert.Equal(automatic?100000:99887,a.StagedGold);Assert.Equal(45,a.StagedDurability);}
        else{Assert.Same(cause,a.Failure);SqliteFailureAssertions.FromAction(a.Tx, cause);}
        if(automatic){Assert.Single(t.DirectFacts);Assert.Single(t.QuotaEvents);Assert.Equal(1,a.StagedUsage);}
        Assert.Empty(f.Sender.Batches);var close=Assert.Single(f.Sender.Outbound.Entries);Assert.True(close.IsClose);Assert.Same(f.Connection,close.Connection);Assert.True(GameplayJob.EndedAJob(cause));Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));AssertNoLedger(f);
        var fault=new{automatic,native,ddl,before,after=await Assets(f),attempts=Attempts(t),facts=t.DirectFacts.ToArray(),events=t.QuotaEvents.ToArray(),cause=cause.ToString(),causeSame=ReferenceEquals(cause,native?a.Tx.CommitFailure!.Cause:a.Failure),sourceCloseExact=ReferenceEquals(f.Connection,close.Connection),inputs=t.Inputs.ToArray()};
        f.Sql("DROP TRIGGER repair_fault");await RepairWire.Send(f,automatic?"auto":"single",sequence:7,trace:t);Assert.Equal((ushort)45,await f.Durability(InventoryContainerType.Equipment,12));Assert.Equal(automatic?1:0,Usage(f));Assert.False(t.Inputs.Last().SequenceIsFresh);Assert.Single(f.Sender.Batches);
        Write("sql",new{fault,afterRetry=await Assets(f),inputs=t.Inputs,frames=Frames(f)});
    }

    [Theory] [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public async Task Ordinary_projection_encoder_and_composer_faults_remain_isolated_Failed(bool automatic,bool composer)
    {
        var t=new Trace{ComposerFault=composer};using var f=Fixture(t);await Seed(f);int calls=0;
        if(!composer)f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);calls++;return t.Cause;});
        var error=await Record.ExceptionAsync(()=>RepairWire.Send(f,automatic?"auto":"single",trace:t).AsTask());Assert.Null(error);
        Assert.Equal(automatic?100000:99887,f.Gold);Assert.Equal(automatic?1:0,Usage(f));Assert.Equal((ushort)45,await f.Durability(InventoryContainerType.Equipment,12));
        Assert.Equal(NotificationOutcome.Failed,Assert.Single(t.Delivery!.Attempts).Outcome);Assert.Single(t.Logs,x=>x.Level==LogLevel.Warning&&ReferenceEquals(x.Error,t.Cause));Assert.Empty(f.Sender.Batches);Assert.Empty(f.Sender.Outbound.Closes);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));AssertNoLedger(f);
        Write("projection",new{automatic,composer,calls,assets=await Assets(f),attempts=Attempts(t),facts=t.DirectFacts,events=t.QuotaEvents,inputs=t.Inputs,frames=Frames(f)});
    }

    [Theory] [InlineData("pre")] [InlineData("normal")] [InlineData("auto-normal")]
    public async Task Actual_rejection_ACK_cause_close_and_chain_distinguish_empty_and_committed_boundaries(string branch)
    {
        var t=new Trace();using var f=Fixture(t);await Seed(f);if(branch=="normal")f.Sql("UPDATE character_stats SET carried_gold=1 WHERE character_id=1");if(branch=="auto-normal")f.Sql("DELETE FROM account_premiums");var before=await Assets(f);int calls=0;
        f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);t.Chain=GameplayJob.Current;calls++;return t.Cause;});
        var error=await Record.ExceptionAsync(()=>RepairWire.Send(f,branch=="auto-normal"?"auto":"single",quick:branch=="pre"?(byte)1:(byte)0,trace:t).AsTask());
        if(branch!="pre"&&t.Contexts.Count>0){var typed=Assert.IsType<TransactionOutcomeUncertainException>(error);Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure,typed.Reason);Assert.Same(t.Cause,typed.InnerException);var request=Assert.IsType<CommandRequestContext.ProtocolNonEffect>(t.Contexts.Single().Request);Assert.True(request.IsClosed);Assert.True(request.HasCommittedUnit);Assert.Empty(request.CommittedParticipants);Assert.Empty((System.Collections.ICollection)typeof(CommandRequestContext).GetField("attempts",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(request)!);}
        else Assert.Same(t.Cause,error);
        if(branch=="pre")Assert.Empty(t.Attempts);else Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(t.Attempts).Tx.Completion);
        Assert.Equal(before,await Assets(f));Assert.True(GameplayJob.EndedAJob(error!));Assert.Equal(1,calls);Assert.Empty(f.Sender.Messages);Assert.Empty(f.Sender.Batches);var close=Assert.Single(f.Sender.Outbound.Entries);Assert.True(close.IsClose);Assert.Same(f.Connection,close.Connection);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));AssertNoLedger(f);
        var chain=t.Chain!.Value;Assert.True(chain.IsInitialized);Assert.Equal(0,chain.Depth);Assert.Null(chain.ParentId);Assert.Equal(GameplayCallSource.Connection,chain.Source);Assert.Equal(f.Connection.ConnectionId,chain.SourceConnection);var log=Assert.Single(t.Logs,x=>x.Level==LogLevel.Error&&ReferenceEquals(x.Error,error));foreach(var pair in new Dictionary<string,object?>{{"RootId",chain.RootId},{"ParentId",chain.ParentId},{"Depth",chain.Depth},{"Source",chain.Source},{"SourceConnection",chain.SourceConnection},{"CommandType",chain.CommandType}})Assert.Equal(pair.Value,log.Fields[pair.Key]);
        Write("refusal",new{branch,before,after=await Assets(f),cause=error!.ToString(),causeSame=ReferenceEquals(error is TransactionOutcomeUncertainException x?x.InnerException:error,t.Cause),typed=TransactionOutcomeUncertainException.Contains(error),reason=(error as TransactionOutcomeUncertainException)?.Reason.ToString(),chain,attempts=Attempts(t),inputs=t.Inputs,sourceCloseExact=ReferenceEquals(f.Connection,close.Connection),endedAJob=GameplayJob.EndedAJob(error),frames=Frames(f)});
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Real_previous_session_query_is_silent_before_domain_or_unit(bool automatic)
    {var t=new Trace();using var f=Fixture(t);await Seed(f);var old=f.Session;f.Select(2);t.Stale=old;var before=await Assets(f);await RepairWire.Send(f,automatic?"auto":"single",trace:t);Assert.Equal(before,await Assets(f));Assert.Empty(t.Attempts);Assert.Empty(f.Sender.Messages);Assert.Empty(f.Sender.Outbound.Closes);Write("stale",new{automatic,before,after=await Assets(f),inputs=t.Inputs,frames=Frames(f),scope="read-only query returns true previous session; IsInWorld uses real registry; wire carries no old-session identity"});}

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Same_sequence_rechecks_broken_state_and_current_gold_or_quota(bool automatic)
    {var t=new Trace();using var f=Fixture(t);await Seed(f);var mode=automatic?"auto":"single";await RepairWire.Send(f,mode,sequence:7,trace:t);var first=f.Gold;await RepairWire.Send(f,mode,sequence:7,trace:t);Assert.Equal(first,f.Gold);Assert.Equal(automatic?1:0,Usage(f));if(automatic)Assert.Equal(4,Assert.IsType<RepairItemResponse>(f.Sender.Messages.Last()).ErrorCode);await f.SeedCore(12,Piece(0),InventoryContainerType.Equipment);await RepairWire.Send(f,mode,sequence:7,trace:t);Assert.Equal(automatic?100000:99774,f.Gold);Assert.Equal(automatic?2:0,Usage(f));Assert.Equal(new[]{true,false,false},t.Inputs.Select(x=>x.SequenceIsFresh));AssertNoLedger(f);Write("sameSeq",new{automatic,assets=await Assets(f),attempts=Attempts(t),facts=t.DirectFacts,events=t.QuotaEvents,inputs=t.Inputs,frames=Frames(f)});}

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Valid_automatic_shape_is_free_and_records_exactly_one_quota_while_ordinary_charges(bool automatic)
    {var t=new Trace();using var f=Fixture(t);await Seed(f);var before=await Assets(f);await RepairWire.Send(f,automatic?"auto":"single",trace:t);Write("mutationAsset",new{automatic,before,after=await Assets(f),gold=f.Gold,usage=Usage(f),facts=t.DirectFacts,events=t.QuotaEvents,inputs=t.Inputs,frames=Frames(f)});Assert.Equal(automatic?100000:99887,f.Gold);Assert.Equal(automatic?1:0,Usage(f));Assert.Equal(automatic?2:1,Assert.Single(f.Sender.Batches).Frames.Length);}

    private static InventoryItemCoreState Piece(ushort durability)=>StackableItemCore.Create(1,3000,1234)with{Durability=durability,Attribute=0xA0,EnchantUpgradeCount=7};
    private static async Task Seed(ShopFixture f){f.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,586,4000000000)");await f.SeedCore(12,Piece(0),InventoryContainerType.Equipment);}
    private static int Usage(ShopFixture f)=>f.Scalar("SELECT COALESCE(SUM(value),0) FROM character_daily_counters WHERE counter_key='premium_auto_repair_used'");
    private void Write(string kind,object data)=>output.WriteLine(JsonSerializer.Serialize(new{task24=true,kind,data}));
    private static object[] Attempts(Trace t)=>t.Attempts.Select(a=>(object)new{completion=a.Tx.Completion.ToString(),a.Completed,failure=a.Failure?.ToString(),a.ForeignKeys,a.Children,a.StagedGold,a.StagedUsage,a.StagedDurability,a.Stage}).ToArray();
    private static string[] Frames(ShopFixture f)=>f.Sender.Outbound.Frames.Select(Convert.ToHexString).ToArray();
    private static ShopFixture Fixture(Trace t)=>new(services=>{
        services.AddSingleton<IEquipmentRepairDefinitionSource>(new Prices());services.AddSingleton<IPremiumServiceDefinitionSource>(new PremiumRules());
        services.AddSingleton<EquipmentRepairDomainService>();services.AddSingleton<IEquipmentRepairDomainService>(p=>new ObserveRepair(p.GetRequiredService<EquipmentRepairDomainService>(),p,t));
        var ev=services.Last(x=>x.ServiceType==typeof(IEventService));services.AddSingleton<ObserveEvents>(p=>new(Resolve<IEventService>(ev,p),p,t));services.AddSingleton<IEventService>(p=>p.GetRequiredService<ObserveEvents>());services.AddSingleton<ICommandProjections>(p=>p.GetRequiredService<ObserveEvents>());
        var clock=(TimeProvider)services.Last(x=>x.ServiceType==typeof(TimeProvider)).ImplementationInstance!;services.AddSingleton<TimeProvider>(p=>new ObserveClock(clock,p,t));var selected=services.Last(x=>x.ServiceType==typeof(ISelectedSessionDomainService));services.AddSingleton<ISelectedSessionDomainService>(p=>QueryProxy.Create(Resolve<ISelectedSessionDomainService>(selected,p),t));services.AddScoped<CommandWorkUnits>();services.AddScoped<ICommandWorkUnits>(p=>new ObserveUnits(p.GetRequiredService<CommandWorkUnits>(),t));services.AddLogging(log=>log.AddProvider(new Logs(t)));
    });
    private static T Resolve<T>(ServiceDescriptor d,IServiceProvider p)where T:class=>(T)(d.ImplementationInstance??d.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,d.ImplementationType!));
    internal sealed class Trace
    {internal readonly List<RepairWireObservation> Inputs=[];internal readonly List<Attempt> Attempts=[];internal readonly List<LogRow> Logs=[];internal readonly List<CommandExecutionContext> Contexts=[];internal readonly List<PremiumUsageRecorded> DirectFacts=[];internal readonly List<UsageQuotaChanged> QuotaEvents=[];internal readonly List<ClockCall> ClockCalls=[];internal GameplayCallChain? Chain;internal bool Native,ComposerFault,Capturing;internal SelectedSessionIdentity? Stale;internal DateTimeOffset PreparedAt;internal IDomainTransaction? ProjectionTransaction;internal NotificationDelivery? Delivery;internal readonly IOException Cause=new("fixed repair actual encoder or composer cause");}
    internal sealed record ClockCall(DateTimeOffset At,bool Open);
    internal sealed record Attempt(DomainTransaction Tx){internal bool Completed;internal Exception? Failure;internal int ForeignKeys,Children,StagedGold,StagedUsage,StagedDurability;internal string? Stage;}
    private sealed class ObserveRepair(IEquipmentRepairDomainService actual,IServiceProvider p,Trace t):IEquipmentRepairDomainService
    {public ValueTask<EquipmentRepairResult> AutoRepairAsync(SelectedSessionIdentity s,CancellationToken ct)=>Run(()=>actual.AutoRepairAsync(s,ct),ct);public ValueTask<EquipmentRepairResult> RepairAsync(SelectedSessionIdentity s,BusinessSlot? slot,CancellationToken ct)=>Run(()=>actual.RepairAsync(s,slot,ct),ct);private async ValueTask<EquipmentRepairResult> Run(Func<ValueTask<EquipmentRepairResult>> action,CancellationToken ct){var tx=Assert.IsType<DomainTransaction>(p.GetRequiredService<IDomainTransactionAccessor>().Current);Assert.False(tx.IsReadOnly);Assert.False(tx.IsSettled);var a=new Attempt(tx);t.Attempts.Add(a);EquipmentRepairResult? r=null;try{r=await action();a.Completed=true;return r;}catch(Exception e){a.Failure=e;throw;}finally{if(a.Failure!=null||r?.Receipt!=null){a.ForeignKeys=await Scalar(p,"PRAGMA foreign_keys",ct);a.Children=t.Native?await Scalar(p,"SELECT count(*) FROM repair_child",ct):0;a.StagedGold=await Scalar(p,"SELECT carried_gold FROM character_stats WHERE character_id=1",ct);a.StagedUsage=await Scalar(p,"SELECT COALESCE(SUM(value),0) FROM character_daily_counters WHERE counter_key='premium_auto_repair_used'",ct);a.Stage=await AssetsIn(p,ct);var assets=JsonDocument.Parse(a.Stage);var items=JsonDocument.Parse(assets.RootElement.GetProperty("items").GetString()!);a.StagedDurability=BinaryPrimitives.ReadUInt16LittleEndian(Convert.FromHexString(items.RootElement[0].GetProperty("item_core").GetString()!).AsSpan(10));}}}}
    private sealed class ObserveEvents(IEventService actual,IServiceProvider p,Trace t):IEventService,ICommandProjections
    {public async ValueTask<Guid> EnqueueAsync<T>(T payload,CancellationToken ct=default)where T:class,IDomainEvent{if(t.Capturing&&payload is UsageQuotaChanged changed){Assert.Same(t.Attempts.Last().Tx,p.GetRequiredService<IDomainTransactionAccessor>().Current);t.QuotaEvents.Add(changed);}return await actual.EnqueueAsync(payload,ct);}public ValueTask DirectAsync<T>(T payload,CancellationToken ct=default)where T:class,IDomainEvent{if(t.Capturing&&payload is PremiumUsageRecorded fact){Assert.Same(t.Attempts.Last().Tx,p.GetRequiredService<IDomainTransactionAccessor>().Current);t.DirectFacts.Add(fact);}return actual.DirectAsync(payload,ct);}public async ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience,T notification,CancellationToken ct=default)where T:class,INotification<T>{if(t.Capturing&&notification is EquipmentRepairCompleted repair){t.ProjectionTransaction=p.GetRequiredService<IDomainTransactionAccessor>().Current;t.PreparedAt=repair.PreparedAt;}var r=await actual.ProjectAsync(audience,notification,ct);if(t.Capturing)t.Delivery=r;return r;}}
    private sealed class ObserveClock(TimeProvider actual,IServiceProvider p,Trace t):TimeProvider{public override DateTimeOffset GetUtcNow(){var now=actual.GetUtcNow();if(t.Capturing)t.ClockCalls.Add(new(now,p.GetRequiredService<IDomainTransactionAccessor>().Current is DomainTransaction{IsSettled:false}));return now;}}
    public class QueryProxy:DispatchProxy
    {private ISelectedSessionDomainService actual=null!;private Trace trace=null!;internal static ISelectedSessionDomainService Create(ISelectedSessionDomainService actual,Trace trace){var x=Create<ISelectedSessionDomainService,QueryProxy>();((QueryProxy)x).actual=actual;((QueryProxy)x).trace=trace;return x;}protected override object? Invoke(MethodInfo? method,object?[]? args){if(method!.Name==nameof(ISelectedSessionDomainService.FindByConnection)&&trace.Stale is{} old)return old;if(method.Name==nameof(ISelectedSessionDomainService.IsInWorld)&&trace.ComposerFault&&trace.Attempts.LastOrDefault()?.Tx.Completion==DomainTransactionCompletion.Committed)throw trace.Cause;try{return method.Invoke(actual,args);}catch(TargetInvocationException e)when(e.InnerException is{} cause){ExceptionDispatchInfo.Throw(cause);throw;}}}
    private sealed class ObserveUnits(ICommandWorkUnits actual,Trace t):ICommandWorkUnits
    {public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull{var req=Assert.IsType<CommandRequestContext.ProtocolNonEffect>(c.Request);Assert.Empty(d.Participants);Assert.Equal(CommandWorkUnitKind.Ordinary,d.Kind);Assert.Equal(CommandFootprintMode.None,d.Footprint);Assert.Same(((RepairItemRequest)req.Message).Metadata,Assert.IsType<ProtocolFrameMetadata.Game>(req.Metadata).Value);t.Contexts.Add(c);return actual.WriteAsync(c,d,a,ct);}public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new InvalidOperationException("Repair uses default ordinary Write.");public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new InvalidOperationException("Repair retains its writable unit.");}
    internal sealed record LogRow(LogLevel Level,Exception? Error,string Message,IReadOnlyDictionary<string,object?> Fields);
    private sealed class Logs(Trace t):ILoggerProvider{public ILogger CreateLogger(string name)=>new Logger(t);public void Dispose(){}private sealed class Logger(Trace t):ILogger{public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;public bool IsEnabled(LogLevel l)=>true;public void Log<TState>(LogLevel l,EventId id,TState state,Exception? error,Func<TState,Exception?,string> format)=>t.Logs.Add(new(l,error,format(state,error),state is IEnumerable<KeyValuePair<string,object?>> fields?fields.ToDictionary(x=>x.Key,x=>x.Value):new Dictionary<string,object?>()));}}
    private sealed class Prices:IEquipmentRepairDefinitionSource{public ValueTask<EquipmentRepairDefinition?> GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult<EquipmentRepairDefinition?>(id==3000?new(1680,3,45):id==100310669?new(0,7,0):null);public ValueTask<(float CostRate,IReadOnlyList<float> UpgradeRates)?> RatesAsync(CancellationToken ct)=>ValueTask.FromResult<(float,IReadOnlyList<float>)?>((0.08415f,new float[]{1,2}));}
    private sealed class PremiumRules:IPremiumServiceDefinitionSource{public IReadOnlyList<int> ReadQuestHelperMinimumDropRates()=>new[]{100,70,30};public DoublePotRules ReadDoublePotRules()=>new(8,new HashSet<int>());public IReadOnlyList<int> ReadGrowthBossEquipmentBonusPercents()=>new[]{20,23,27,30};public double ReadBlackDiamondExperienceBonusRate()=>.05;public double ReadGrowthExperienceBonusRate()=>.2;public PremiumContractLevels ReadContractLevels()=>new(5,10);public int ReadFreeWeaknessDailyLimit()=>10;public int ReadFreeGoldCardDailyLimit()=>10;public int ReadDungeonBuffDailyLimit()=>10;public int ReadAutoRepairDailyLimit()=>6;}
    private static void AssertNoLedger(ShopFixture f){var table=typeof(Dfo.Server.Application.Effects.EffectOperationLedger).GetField("ledgers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Services.GetRequiredService<Dfo.Server.Application.Effects.EffectOperationLedger>())!;object?[] args=[f.Connection,null];Assert.False((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,args)!);}
    internal static Task<string> Assets(ShopFixture f)=>TestGameplay.RunAsync(f.Services,async()=>await AssetsIn(f.Services,default));
    private static async ValueTask<string> AssetsIn(IServiceProvider p,CancellationToken ct){var data=new SortedDictionary<string,string>();foreach(var table in new[]{"items","item_slots","characters","character_stats","character_container_state","account_cargo_state","account_premiums","character_daily_counters","domain_events","domain_event_deliveries"})data[table]=await Rows(p,"SELECT * FROM "+table,ct);return JsonSerializer.Serialize(data);}
    private static ValueTask<string> Rows(IServiceProvider p,string sql,CancellationToken ct)=>p.GetRequiredService<SqliteDatabase>().ReadAsync(async(cmd,token)=>{cmd.CommandText=sql;await using var r=await cmd.ExecuteReaderAsync(token);var rows=new List<string>();while(await r.ReadAsync(token))rows.Add(JsonSerializer.Serialize(Enumerable.Range(0,r.FieldCount).ToDictionary(i=>r.GetName(i),i=>r.IsDBNull(i)?null:r.GetValue(i)is byte[] blob?Convert.ToHexString(blob):Convert.ToString(r.GetValue(i)))));rows.Sort(StringComparer.Ordinal);return "["+string.Join(",",rows)+"]";},ct);
    private static ValueTask<int> Scalar(IServiceProvider p,string sql,CancellationToken ct)=>p.GetRequiredService<SqliteDatabase>().ReadAsync(async(cmd,token)=>{cmd.CommandText=sql;return Convert.ToInt32(await cmd.ExecuteScalarAsync(token));},ct);
}
internal sealed record RepairWireObservation(byte Selector,ushort Type,ushort Sequence,long? InboundOrdinal,bool SequenceIsFresh,int? FullBodyLength,string FullBodySha256,string Body,string CLR,int Logical,DateTimeOffset Started);
internal static class RepairWire
{
    private sealed class State{internal ushort Sequence;internal readonly GameInboundFrameCounter Counter=new();}private static readonly ConditionalWeakTable<ShopFixture,State> states=new();
    internal static async ValueTask<IClientNetworkMessage> Send(ShopFixture f,string mode,InventoryContainerType container=InventoryContainerType.Equipment,short slot=12,byte quick=0,ushort? sequence=null,RepairItemProtocolCommandFlowTests.Trace? trace=null)
    {byte[] body;if(mode=="auto")body=Convert.FromHexString("00FFFFFFFF0100");else{body=new byte[8];body[0]=(byte)(mode=="personal-all"?InventoryContainerType.PersonalCargo:mode=="all"?InventoryContainerType.Main:container);BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1),mode is "all" or "personal-all"?(short)-1:slot);BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(3),-1);body[7]=quick;}var state=states.GetOrCreateValue(f);var seq=sequence??++state.Sequence;var(ordinal,fresh)=state.Counter.Observe(seq);var metadata=new GameMessageMetadata(1,0x0017,0,seq){InboundOrdinal=ordinal,SequenceIsFresh=fresh};metadata=metadata.WithRawBody(body);var reg=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1&&x.Type==0x0017);var decoded=Assert.IsType<RepairItemRequest>(((IGameMessageDecoder)f.Services.GetRequiredService(reg.DecoderType)).Decode(metadata,body));Assert.Same(metadata,decoded.Metadata);Assert.Equal(body.Length,metadata.FullBodyLength);Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)),metadata.FullBodySha256);trace?.Inputs.Add(new(metadata.Selector,metadata.Type,metadata.Sequence,metadata.InboundOrdinal,metadata.SequenceIsFresh,metadata.FullBodyLength,metadata.FullBodySha256!,Convert.ToHexString(body),decoded.GetType().FullName!,(int)decoded.MessageType,DateTimeOffset.UtcNow));if(trace!=null)trace.Capturing=true;try{await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),decoded,default);return decoded;}finally{if(trace!=null)trace.Capturing=false;}}
}
