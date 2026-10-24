using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.SellItem;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class SellItemProtocolCommandFlowTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)] [InlineData(10)]
    public async Task Real_pile_sale_preserves_complete_assets_and_one_exact_ACK_after_commit(int count)
    {
        var trace=new Trace();using var f=Fixture(trace);await f.Seed(65,5000,10);var before=await Assets(f);
        await SellWire.Send(f,65,count,observations:trace.Inputs,trace:trace);
        var a=Assert.Single(trace.Attempts);Assert.True(a.Completed);Assert.Null(a.Failure);Assert.Same(a.Transaction,a.ClockTransaction);
        Assert.Equal(DomainTransactionCompletion.Committed,a.Transaction.Completion);Assert.Equal(10-count,f.Count(5000));Assert.Equal(100000+31*count,f.Gold);
        var ack=Assert.IsType<SellItemResponse>(Assert.Single(f.Sender.Messages));Assert.Equal(new GameSlot(InventoryContainerType.Main,65),ack.Slot);Assert.Equal(count,ack.Count);Assert.Equal(f.Gold,ack.Gold);
        var batch=Assert.Single(f.Sender.Batches);Assert.Same(f.Connection,batch.Connection);Assert.Single(batch.Frames);Assert.Equal((ushort)0x0016,Type(batch.Frames[0]));AssertNoLedger(f);
        output.WriteLine(JsonSerializer.Serialize(new{task22Sale=true,count,before,after=await Assets(f),stage=a.Stage,completion=a.Transaction.Completion.ToString(),input=trace.Inputs.Single(),frames=batch.Frames.Select(Convert.ToHexString),clockInSameWrite=ReferenceEquals(a.Transaction,a.ClockTransaction)}));
    }
    [Fact]
    public async Task Real_equipment_sale_uses_one_UID_not_InstanceValue_or_durability_as_quantity()
    {
        var trace=new Trace();using var f=Fixture(trace);var core=StackableItemCore.Create(1,3000,178234)with{Durability=45,EnchantCardId=42};await f.SeedCore(9,core);var before=await Assets(f);var uid=f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=9");
        await SellWire.Send(f,9,1,trace:trace,observations:trace.Inputs);Assert.Equal(102332,f.Gold);Assert.Equal(0,f.Count(3000));Assert.Equal(1,Assert.IsType<SellItemResponse>(Assert.Single(f.Sender.Messages)).Count);
        output.WriteLine(JsonSerializer.Serialize(new{task22Equipment=true,uid,before,after=await Assets(f),input=trace.Inputs.Single(),frames=f.Sender.Outbound.Frames.Select(Convert.ToHexString)}));
    }
    [Theory]
    [InlineData(0)] [InlineData(30)]
    public async Task Real_gold_cap_exception_rolls_back_even_a_partial_gold_write_before_refusal(int space)
    {
        var trace=new Trace();using var f=Fixture(trace,100000+space);await f.Seed(65,5000,10);var before=await Assets(f);
        await SellWire.Send(f,65,1,trace:trace,observations:trace.Inputs);
        var a=Assert.Single(trace.Attempts);Assert.IsType<ItemSaleRejectedException>(a.Failure);Assert.Equal(ItemSaleRejection.GoldLimit,((ItemSaleRejectedException)a.Failure!).Reason);
        Assert.Equal(100000+space,a.StagedGold);Assert.Equal(10,a.StagedCount);
        var after=await Assets(f);output.WriteLine(JsonSerializer.Serialize(new{task22CapActual=true,space,after,ack=f.Sender.Messages.Single(),frames=f.Sender.Outbound.Frames.Select(Convert.ToHexString),actualGold=f.Gold,actualCount=f.Count(5000),a.StagedGold,a.StagedCount,completion=a.Transaction.Completion.ToString()}));
        Assert.Equal(100000,f.Gold);Assert.Equal(before,after);Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,a.Transaction.Completion);Assert.Equal(10,f.Count(5000));Assert.Equal(new SellItemResponse(null,ErrorCode:22),Assert.Single(f.Sender.Messages));Assert.Empty(f.Sender.Outbound.Closes);AssertNoLedger(f);
        output.WriteLine(JsonSerializer.Serialize(new{task22Cap=true,space,before,after,stage=a.Stage,a.StagedGold,a.StagedCount,completion=a.Transaction.Completion.ToString(),cause=a.Failure!.ToString(),frames=f.Sender.Outbound.Frames.Select(Convert.ToHexString),input=trace.Inputs.Single()}));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_SQL_action_and_native_Commit_failures_keep_cause_full_rollback_and_same_sequence_retry(bool native)
    {
        var trace=new Trace{Native=native,ObserveSql=true};using var f=Fixture(trace);await f.Seed(65,5000,10);var before=await Assets(f);
        var ddl=native?"CREATE TABLE sale_parent(id INTEGER PRIMARY KEY);CREATE TABLE sale_child(id INTEGER REFERENCES sale_parent(id) DEFERRABLE INITIALLY DEFERRED);CREATE TRIGGER sale_fault AFTER UPDATE OF count ON item_slots WHEN NEW.owner_scope='character' AND NEW.owner_id=1 AND NEW.list_type=0 AND NEW.slot_index=65 BEGIN INSERT INTO sale_child VALUES(404);END;":"CREATE TRIGGER sale_fault BEFORE UPDATE OF count ON item_slots WHEN NEW.owner_scope='character' AND NEW.owner_id=1 AND NEW.list_type=0 AND NEW.slot_index=65 BEGIN SELECT RAISE(ABORT,'sale action SQL failure');END;";
        f.Sql(ddl);var thrown=await Record.ExceptionAsync(()=>SellWire.Send(f,65,1,7,trace:trace,observations:trace.Inputs).AsTask());var cause=Assert.IsType<SqliteException>(thrown);Assert.Equal(19,cause.SqliteErrorCode);Assert.Equal(native?787:1811,cause.SqliteExtendedErrorCode);
        var a=Assert.Single(trace.Attempts);Assert.Equal(1,a.ForeignKeys);Assert.Equal(native,a.Completed);Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,a.Transaction.Completion);Assert.Equal(before,await Assets(f));
        if(native){Assert.True(a.Children>0);Assert.Equal(100031,a.StagedGold);Assert.Equal(9,a.StagedCount);Assert.Same(cause,a.Transaction.CommitFailure!.Cause);Assert.Contains("SqliteTransaction.Commit",cause.StackTrace!,StringComparison.Ordinal);}else{Assert.Same(cause,a.Failure);Assert.DoesNotContain("SqliteTransaction.Commit",cause.StackTrace!,StringComparison.Ordinal);}
        Assert.Empty(f.Sender.Batches);var close=Assert.Single(f.Sender.Outbound.Entries);Assert.True(close.IsClose);Assert.Same(f.Connection,close.Connection);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));AssertNoLedger(f);
        var fault=new{native,ddl,before,stage=a.Stage,a.ForeignKeys,a.Children,a.Completed,a.StagedGold,a.StagedCount,completion=a.Transaction.Completion.ToString(),cause=cause.ToString(),causeSame=ReferenceEquals(cause,native?a.Transaction.CommitFailure!.Cause:a.Failure),sourceCloseExact=ReferenceEquals(f.Connection,close.Connection),input=trace.Inputs.Single()};
        f.Sql("DROP TRIGGER sale_fault");await SellWire.Send(f,65,1,7,trace:trace,observations:trace.Inputs);Assert.Equal(2,trace.Attempts.Count);Assert.Equal(9,f.Count(5000));Assert.Equal(100031,f.Gold);Assert.False(trace.Inputs.Last().SequenceIsFresh);Assert.Single(f.Sender.Batches);
        output.WriteLine(JsonSerializer.Serialize(new{task22SQL=true,fault,afterRetry=await Assets(f),retry=trace.Inputs.Last(),frames=f.Sender.Outbound.Frames.Select(Convert.ToHexString)}));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Successful_projection_encoder_or_composer_failure_is_isolated_as_Warning_Failed(bool composer)
    {
        var trace=new Trace{ComposerFault=composer};using var f=Fixture(trace);await f.Seed(65,5000,10);var calls=0;
        if(!composer)f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);calls++;return trace.Cause;});
        var error=await Record.ExceptionAsync(()=>SellWire.Send(f,65,1,trace:trace,observations:trace.Inputs).AsTask());Assert.Null(error);Assert.Equal(9,f.Count(5000));Assert.Equal(100031,f.Gold);Assert.Empty(f.Sender.Batches);Assert.Empty(f.Sender.Outbound.Closes);
        Assert.Single(trace.Logs,x=>x.Level==LogLevel.Warning&&ReferenceEquals(x.Error,trace.Cause));Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Attempts).Transaction.Completion);AssertNoLedger(f);
        output.WriteLine(JsonSerializer.Serialize(new{task22Projection=true,composer,calls,assets=await Assets(f),completion=trace.Attempts.Single().Transaction.Completion.ToString(),input=trace.Inputs.Single(),cause=trace.Cause.ToString(),warningSame=true,closes=0,blocked=false}));
    }
    [Theory]
    [InlineData("pre")] [InlineData("normal")] [InlineData("rollback")]
    public async Task Three_refusal_ACK_faults_keep_their_actual_transaction_classification_and_source(string branch)
    {
        var trace=new Trace();using var f=Fixture(trace,branch=="rollback"?100030:1000000);await f.Seed(65,5000,10);var before=await Assets(f);var calls=0;
        f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);calls++;return trace.Cause;});
        var error=await Record.ExceptionAsync(()=>SellWire.Send(f,65,branch=="normal"?0:1,7,sum:branch=="pre"?0:null,trace:trace,observations:trace.Inputs).AsTask());
        if(branch=="normal")
        {
            var typed=Assert.IsType<TransactionOutcomeUncertainException>(error);
            Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure,typed.Reason);Assert.Same(trace.Cause,typed.InnerException);
            var request=Assert.IsType<CommandRequestContext.ProtocolNonEffect>(Assert.Single(trace.Contexts).Request);
            Assert.True(request.IsClosed);Assert.True(request.HasCommittedUnit);Assert.Empty(request.CommittedParticipants);
            Assert.Empty((System.Collections.ICollection)typeof(CommandRequestContext).GetField("attempts",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(request)!);
            Assert.NotNull(trace.Chain);var chain=trace.Chain.Value;Assert.True(chain.IsInitialized);Assert.Equal(nameof(GameSellItemCommand),chain.CommandType);Assert.Equal(0,chain.Depth);Assert.Null(chain.ParentId);Assert.Equal(GameplayCallSource.Connection,chain.Source);Assert.Equal(f.Connection.ConnectionId,chain.SourceConnection);
            var log=Assert.Single(trace.Logs,x=>x.Level==LogLevel.Error&&ReferenceEquals(x.Error,typed));Assert.Equal(chain.RootId,log.Fields["RootId"]);Assert.Equal(chain.ParentId,log.Fields["ParentId"]);Assert.Equal(chain.Depth,log.Fields["Depth"]);Assert.Equal(chain.Source,log.Fields["Source"]);Assert.Equal(chain.SourceConnection,log.Fields["SourceConnection"]);Assert.Equal(nameof(GameSellItemCommand),log.Fields["CommandType"]);
        }
        else Assert.Same(trace.Cause,error);
        Assert.True(GameplayJob.EndedAJob(error!));Assert.Equal(before,await Assets(f));Assert.Equal(1,calls);Assert.Empty(f.Sender.Messages);Assert.Empty(f.Sender.Batches);
        var close=Assert.Single(f.Sender.Outbound.Entries);Assert.True(close.IsClose);Assert.Same(f.Connection,close.Connection);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));AssertNoLedger(f);
        if(branch=="pre")Assert.Empty(trace.Attempts);else Assert.Equal(branch=="normal"?DomainTransactionCompletion.Committed:DomainTransactionCompletion.ConfirmedRollback,Assert.Single(trace.Attempts).Transaction.Completion);
        output.WriteLine(JsonSerializer.Serialize(new{task22Refusal=true,branch,before,after=await Assets(f),cause=error!.ToString(),causeSame=ReferenceEquals(error is TransactionOutcomeUncertainException envelope?envelope.InnerException:error,trace.Cause),typed=TransactionOutcomeUncertainException.Contains(error),reason=(error as TransactionOutcomeUncertainException)?.Reason.ToString(),chain=trace.Chain,completion=trace.Attempts.SingleOrDefault()?.Transaction.Completion.ToString(),calls,input=trace.Inputs.Single(),sourceCloseExact=ReferenceEquals(f.Connection,close.Connection),blocked=false,endedAJob=GameplayJob.EndedAJob(error)}));
    }
    [Fact]
    public async Task Same_raw_sequence_executes_two_sales_and_exhausted_item_is_not_replayed()
    {
        var trace=new Trace();using var f=Fixture(trace);await f.Seed(65,5000,3);await SellWire.Send(f,65,1,7,trace:trace,observations:trace.Inputs);await SellWire.Send(f,65,1,7,trace:trace,observations:trace.Inputs);
        Assert.Equal(1,f.Count(5000));Assert.Equal(100062,f.Gold);Assert.Equal(2,f.Sender.Messages.Count);Assert.All(f.Sender.Messages,x=>Assert.NotNull(Assert.IsType<SellItemResponse>(x).Slot));Assert.Equal(new[]{true,false},trace.Inputs.Select(x=>x.SequenceIsFresh));
        await SellWire.Send(f,65,1,7,trace:trace,observations:trace.Inputs);await SellWire.Send(f,65,1,7,trace:trace,observations:trace.Inputs);Assert.Equal(100093,f.Gold);Assert.Equal(17,Assert.IsType<SellItemResponse>(f.Sender.Messages.Last()).ErrorCode);AssertNoLedger(f);
    }
    [Fact]
    public async Task Actual_wire_stale_query_keeps_original_character_assets_and_normal_refusal()
    {
        var trace=new Trace();using var f=Fixture(trace);await f.Seed(65,5000,10);var old=f.Session;f.Select(2);trace.Stale=old;var before=await Assets(f);
        await SellWire.Send(f,65,1,trace:trace,observations:trace.Inputs);Assert.Equal(before,await Assets(f));Assert.Equal(new SellItemResponse(null,ErrorCode:1),Assert.Single(f.Sender.Messages));Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Attempts).Transaction.Completion);Assert.Empty(f.Sender.Outbound.Closes);
        output.WriteLine(JsonSerializer.Serialize(new{task22Stale=true,before,after=await Assets(f),input=trace.Inputs.Single(),completion=trace.Attempts.Single().Transaction.Completion.ToString(),frames=f.Sender.Outbound.Frames.Select(Convert.ToHexString),scope="read-only query proxy returns real previous session; IsInWorld asks actual selected registry; wire has no old identity field"}));
    }
    internal static ShopFixture CreateFixture(Trace trace,int cap=1000000)=>Fixture(trace,cap);
    private static ShopFixture Fixture(Trace trace,int cap=1000000)=>new(services=>{
        services.AddSingleton(trace);services.AddSingleton<IItemSaleDefinitionSource,Prices>();services.AddSingleton<IGoldCarryLimitSource>(new GoldLimit(cap));
        services.AddSingleton<ItemSaleDomainService>();services.AddSingleton<IItemSaleDomainService>(p=>new ObserveSales(p.GetRequiredService<ItemSaleDomainService>(),p,trace));
        var clock=(TimeProvider)services.Last(x=>x.ServiceType==typeof(TimeProvider)).ImplementationInstance!;services.AddSingleton<TimeProvider>(p=>new ObserveClock(clock,p,trace));
        var selected=services.Last(x=>x.ServiceType==typeof(ISelectedSessionDomainService)).ImplementationFactory!;services.AddSingleton<ISelectedSessionDomainService>(p=>QueryProxy.Create((ISelectedSessionDomainService)selected(p),trace));
        services.AddScoped<CommandWorkUnits>();services.AddScoped<ICommandWorkUnits>(p=>new ObserveUnits(p.GetRequiredService<CommandWorkUnits>(),trace));services.AddLogging(log=>log.AddProvider(new Logs(trace)));
    });
    internal sealed class Trace
    {
        internal readonly List<SellWireObservation> Inputs=[];internal readonly List<Attempt> Attempts=[];internal readonly List<LogRow> Logs=[];internal readonly List<CommandExecutionContext> Contexts=[];
        internal GameplayCallChain? Chain;internal bool ObserveSql,Native,ComposerFault,Capturing,AwaitClock;internal DomainTransaction? ClockTransaction;internal DateTimeOffset ClockValue;internal SelectedSessionIdentity? Stale;internal readonly IOException Cause=new("fixed sell actual encoder or composer cause");
    }
    internal sealed record Attempt(DomainTransaction Transaction,DomainTransaction ClockTransaction)
    {internal bool Completed;internal ItemSaleResult? Result;internal Exception? Failure;internal AssetState? Stage;internal int ForeignKeys,Children,StagedGold,StagedCount;}
    private sealed class ObserveSales(IItemSaleDomainService actual,IServiceProvider services,Trace trace):IItemSaleDomainService
    {
        public async ValueTask<ItemSaleResult> SellAsync(SelectedSessionIdentity session,BusinessSlot slot,int count,int npcId,DateTimeOffset now,CancellationToken ct)
        {
            var tx=Assert.IsType<DomainTransaction>(services.GetRequiredService<IDomainTransactionAccessor>().Current);Assert.False(tx.IsReadOnly);Assert.False(tx.IsSettled);Assert.Same(trace.ClockTransaction,tx);Assert.Equal(trace.ClockValue,now);var attempt=new Attempt(tx,trace.ClockTransaction!);trace.Attempts.Add(attempt);
            try{var result=await actual.SellAsync(session,slot,count,npcId,now,ct);attempt.Completed=true;attempt.Result=result;return result;}catch(Exception error){attempt.Failure=error;throw;}
            finally{if(attempt.Failure is not null || attempt.Result?.Receipt is not null){attempt.ForeignKeys=await Scalar(services,"PRAGMA foreign_keys",ct);attempt.Children=trace.Native?await Scalar(services,"SELECT count(*) FROM sale_child",ct):0;attempt.Stage=await AssetsIn(services,ct);attempt.StagedGold=await Scalar(services,"SELECT carried_gold FROM character_stats WHERE character_id=1",ct);attempt.StagedCount=await Scalar(services,"SELECT COALESCE(SUM(count),0) FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=65",ct);}}
        }
    }
    private sealed class ObserveClock(TimeProvider actual,IServiceProvider services,Trace trace):TimeProvider
    {public override DateTimeOffset GetUtcNow(){var now=actual.GetUtcNow();if(trace.Capturing&&trace.AwaitClock&&services.GetRequiredService<IDomainTransactionAccessor>().Current is DomainTransaction tx){Assert.False(tx.IsReadOnly);Assert.False(tx.IsSettled);trace.ClockTransaction=tx;trace.ClockValue=now;trace.AwaitClock=false;}return now;}}
    public class QueryProxy:DispatchProxy
    {
        private ISelectedSessionDomainService actual=null!;private Trace trace=null!;
        internal static ISelectedSessionDomainService Create(ISelectedSessionDomainService actual,Trace trace){var x=Create<ISelectedSessionDomainService,QueryProxy>();((QueryProxy)x).actual=actual;((QueryProxy)x).trace=trace;return x;}
        protected override object? Invoke(MethodInfo? method,object?[]? args){if(method!.Name==nameof(ISelectedSessionDomainService.FindByConnection)&&trace.Stale is {} stale)return stale;if(method.Name==nameof(ISelectedSessionDomainService.IsInWorld)&&trace.ComposerFault&&trace.Attempts.LastOrDefault()?.Transaction.Completion==DomainTransactionCompletion.Committed)throw trace.Cause;try{return method.Invoke(actual,args);}catch(TargetInvocationException error)when(error.InnerException is {} cause){ExceptionDispatchInfo.Throw(cause);throw;}}
    }
    private sealed class ObserveUnits(ICommandWorkUnits actual,Trace trace):ICommandWorkUnits
    {
        public ValueTask<TResult> WriteAsync<TResult>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CommandWorkUnit,CancellationToken,ValueTask<TResult>> action,CancellationToken ct=default)where TResult:notnull
        {var plain=Assert.IsType<CommandRequestContext.ProtocolNonEffect>(context.Request);Assert.Empty(declaration.Participants);Assert.Equal(CommandWorkUnitKind.Ordinary,declaration.Kind);Assert.Equal(CommandFootprintMode.None,declaration.Footprint);Assert.Same(((SellItemRequest)plain.Message).Metadata,Assert.IsType<ProtocolFrameMetadata.Game>(plain.Metadata).Value);trace.Contexts.Add(context);trace.Chain=GameplayJob.Current;return actual.WriteAsync(context,declaration,action,ct);}
        public ValueTask<CommandWriteResult<TResult>> WriteResultAsync<TResult>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CommandWorkUnit,CancellationToken,ValueTask<TResult>> action,CancellationToken ct=default)where TResult:notnull=>throw new InvalidOperationException("Sale retains ordinary Write, not commit-result recovery.");
        public ValueTask<TResult> ReadOnlyAsync<TResult>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CancellationToken,ValueTask<TResult>> action,CancellationToken ct=default)where TResult:notnull=>throw new InvalidOperationException("Sale must keep its write transaction.");
    }
    internal sealed record LogRow(LogLevel Level,Exception? Error,string Message,IReadOnlyDictionary<string,object?> Fields);
    private sealed class Logs(Trace trace):ILoggerProvider
    {public ILogger CreateLogger(string name)=>new Logger(trace);public void Dispose(){}private sealed class Logger(Trace trace):ILogger{public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;public bool IsEnabled(LogLevel level)=>true;public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> formatter)=>trace.Logs.Add(new(level,error,formatter(state,error),state is IEnumerable<KeyValuePair<string,object?>> fields?fields.ToDictionary(x=>x.Key,x=>x.Value):new Dictionary<string,object?>()));}}
    private sealed class Prices:IItemSaleDefinitionSource
    {public ValueTask<ItemSaleDefinition?> GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult<ItemSaleDefinition?>(id==3000?new(12960,200,true,true,45):id==5000?new(155,200,true):null);}
    private sealed class GoldLimit(int cap):IGoldCarryLimitSource{public int BaseLimitFor(int level)=>cap;}
    private static ushort Type(byte[] frame)=>BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
    private static void AssertNoLedger(ShopFixture f)
    {var table=typeof(Dfo.Server.Application.Effects.EffectOperationLedger).GetField("ledgers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Services.GetRequiredService<Dfo.Server.Application.Effects.EffectOperationLedger>())!;object?[] args=[f.Connection,null];Assert.False((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,args)!);}
    internal sealed record AssetState(string Items,string Slots,string Stats,string Parameters,string Cargo,string Events,string Deliveries);
    private static Task<AssetState> Assets(ShopFixture f)=>TestGameplay.RunAsync(f.Services,async()=>await AssetsIn(f.Services,default));
    private static async ValueTask<AssetState> AssetsIn(IServiceProvider s,CancellationToken ct)=>new(await Rows(s,"SELECT * FROM items ORDER BY item_uid",ct),await Rows(s,"SELECT * FROM item_slots ORDER BY owner_scope,owner_id,list_type,slot_index",ct),await Rows(s,"SELECT * FROM character_stats ORDER BY character_id",ct),await Rows(s,"SELECT * FROM character_container_state ORDER BY character_id,list_type",ct),await Rows(s,"SELECT * FROM account_cargo_state ORDER BY account_id",ct),await Rows(s,"SELECT * FROM domain_events ORDER BY event_id",ct),await Rows(s,"SELECT * FROM domain_event_deliveries",ct));
    private static ValueTask<string> Rows(IServiceProvider s,string sql,CancellationToken ct)=>s.GetRequiredService<SqliteDatabase>().ReadAsync(async(cmd,token)=>{cmd.CommandText=sql;await using var r=await cmd.ExecuteReaderAsync(token);var rows=new List<object>();while(await r.ReadAsync(token))rows.Add(Enumerable.Range(0,r.FieldCount).ToDictionary(i=>r.GetName(i),i=>r.IsDBNull(i)?null:r.GetValue(i)is byte[] blob?Convert.ToHexString(blob):Convert.ToString(r.GetValue(i))));return JsonSerializer.Serialize(rows);},ct);
    private static ValueTask<int> Scalar(IServiceProvider s,string sql,CancellationToken ct)=>s.GetRequiredService<SqliteDatabase>().ReadAsync(async(cmd,token)=>{cmd.CommandText=sql;return Convert.ToInt32(await cmd.ExecuteScalarAsync(token));},ct);
}
internal sealed record SellWireObservation(byte Selector,ushort Type,ushort Sequence,long? InboundOrdinal,bool SequenceIsFresh,int? FullBodyLength,string FullBodySha256,string Body,string CLR,int Logical);
internal static class SellWire
{
    private sealed class State{internal ushort Sequence;internal readonly GameInboundFrameCounter Counter=new();}private static readonly ConditionalWeakTable<ShopFixture,State> states=new();
    internal static async ValueTask Sell(ShopFixture f,int slot,int count,InventoryContainerType container=InventoryContainerType.Main,int npc=2,int? sum=null)=>await Send(f,slot,count,container:container,npc:npc,sum:sum);
    internal static async ValueTask<IClientNetworkMessage> Send(ShopFixture f,int slot,int count,ushort? sequence=null,InventoryContainerType container=InventoryContainerType.Main,int npc=2,int? sum=null,List<SellWireObservation>? observations=null,SellItemProtocolCommandFlowTests.Trace? trace=null)
    {
        trace ??= f.Services.GetService<SellItemProtocolCommandFlowTests.Trace>();
        byte[] body=new byte[13];body[0]=(byte)container;BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1),checked((short)slot));BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(3),checked((short)count));BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5),npc);BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9),sum??(byte)container+slot+count);
        var state=states.GetOrCreateValue(f);var seq=sequence??++state.Sequence;var(ordinal,fresh)=state.Counter.Observe(seq);var metadata=new GameMessageMetadata(1,0x0016,0,seq){InboundOrdinal=ordinal,SequenceIsFresh=fresh};metadata=metadata.WithRawBody(body);var reg=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1&&x.Type==0x0016);var decoded=Assert.IsType<SellItemRequest>(((IGameMessageDecoder)f.Services.GetRequiredService(reg.DecoderType)).Decode(metadata,body));Assert.Same(metadata,decoded.Metadata);Assert.Equal(body.Length,metadata.FullBodyLength);Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)),metadata.FullBodySha256);observations?.Add(new(metadata.Selector,metadata.Type,metadata.Sequence,metadata.InboundOrdinal,metadata.SequenceIsFresh,metadata.FullBodyLength,metadata.FullBodySha256!,Convert.ToHexString(body),decoded.GetType().FullName!,(int)decoded.MessageType));if(trace is not null){trace.Capturing=true;trace.AwaitClock=true;}
        try{await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),decoded,default);return decoded;}finally{if(trace is not null)trace.Capturing=false;}
    }
}
