using Dfo.Server.Application.Tests.Support;
using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.CollectionBox;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class CollectionBoxProtocolCommandFlowTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_decoder_Select4_and_Put1_retain_encoder_cause_entry_ordinal_current_replay_and_no_fence(bool put)
    {
        var trace = new Trace(); using var f = Fixture(trace);
        await f.SeedCore(65, StackableItemCore.Create(2,1000,3) with { Durability=17 });
        var before = await Assets(f);
        var cause = new IOException("fixed actual CollectionBox encoder cause");
        var fault = true; var calls = 0; var targets = new List<NetworkConnectionContext>();
        f.Sender.Outbound.Encoder.Fail((connection, _) => {
            calls++; targets.Add(connection);
            return fault && f.Sender.Outbound.BatchInFlight ? cause : null;
        });
        Assert.Same(f.Services.GetRequiredService<INetworkMessageSender>(), f.Services.GetRequiredService<ICommandMessageOutbox>());
        var metadata = new GameMessageMetadata(1,put ? (ushort)0x03E6 : (ushort)0x03E5,0,7);
        byte[] body = put ? [1,0,65,0,0xE8,3,0,0] : [4];
        var thrown = await Record.ExceptionAsync(() => SendRaw(f,trace,metadata,body).AsTask());
        Assert.Same(cause,thrown); Assert.Equal(1,calls); Assert.All(targets,target=>Assert.Same(f.Connection,target));
        var attempt = Assert.Single(trace.Attempts); var entry = attempt.Entry; var ordinal = entry.Ordinal;
        Assert.Same(entry,Entry(f)); Assert.Equal(EffectOperationState.Committed,entry.State);
        Assert.Equal(DomainTransactionCompletion.Committed,attempt.Transaction.Completion);
        Assert.Equal(put ? ReplayProjectionNeeds.Inventory|ReplayProjectionNeeds.CollectionBox : ReplayProjectionNeeds.CollectionBox,entry.Projection!.Needs);
        Assert.Equal(put?1:4,f.Scalar("SELECT selected_box_index FROM character_collectbox_state WHERE character_id=1"));
        Assert.Equal(put?2:3,f.Scalar("SELECT count FROM item_slots WHERE list_type=0 AND slot_index=65"));
        Assert.Equal(put?1:0,f.Scalar("SELECT count(*) FROM item_slots WHERE list_type=241"));
        Assert.Empty(f.Sender.Batches); Assert.Empty(f.Sender.Messages);
        var close = Assert.Single(f.Sender.Outbound.Entries); Assert.True(close.IsClose); Assert.Same(f.Connection,close.Connection);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        var committed = await Assets(f);
        f.Sql("UPDATE character_collectbox_state SET selected_box_index="+(put?4:1)+" WHERE character_id=1");
        if(put) await f.SeedCore(65,StackableItemCore.Create(2,1000,6) with { Durability=19 });
        var current = await Assets(f); fault=false; var previousReads=trace.Reads;
        await SendRaw(f,trace,metadata,body);
        Assert.Single(trace.Attempts); Assert.True(trace.Reads>previousReads);
        Assert.Same(entry,Entry(f)); Assert.Equal(ordinal,entry.Ordinal); Assert.Equal(EffectOperationState.Committed,entry.State);
        Assert.Equal(current,await Assets(f)); Assert.DoesNotContain(f.Sender.Messages,m=>m.MessageType==ServerMessageType.GameCollectionBox);
        var replay=Assert.Single(f.Sender.Batches); Assert.Same(f.Connection,replay.Connection);
        Assert.All(replay.Frames,frame=>Assert.Equal(0,frame[0]));
        var boxes=replay.Frames.Where(frame=>BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))==0x03EB).ToArray();
        Assert.Equal(new byte[]{1,4,put?(byte)4:(byte)1},boxes.Select(frame=>frame[15]));
        Assert.Equal(new byte[]{0,0,1},boxes.Select(frame=>frame[16]));
        if(put) Assert.Contains(replay.Frames,frame=>BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))==0x000D);
        else Assert.Equal(3,replay.Frames.Length);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Single(f.Sender.Outbound.Closes);
        output.WriteLine(JsonSerializer.Serialize(new { task20OldEncoder=true,put,before,committed,current, body=Convert.ToHexString(body),
            entry.Selector,entry.Type,entry.Sequence,entry.Length,entry.Hash,ordinal, sameEntry=true, state=entry.State.ToString(),
            completion=attempt.Transaction.Completion.ToString(), cause=thrown!.ToString(), causeSame=true, actualEncoderCalls=calls,
            trace.Reads,decoder=trace.Inputs.Select(x=>new {action=x.Action.ToString(),x.BoxId,x.SourceSlot,x.ItemId,x.Metadata.InboundOrdinal,x.Metadata.SequenceIsFresh,x.Metadata.FullBodyLength,x.Metadata.FullBodySha256}),
            replayFrames=replay.Frames.Select(Convert.ToHexString),sourceCloseExact=true,blocked=false }));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_decoder_Put_SQL_action_and_native_commit_keep_raw_cause_complete_assets_and_same_entry_retry(bool native)
    {
        var trace=new Trace {Native=native,ObserveSql=true}; using var f=Fixture(trace);
        await f.SeedCore(65,StackableItemCore.Create(2,1000,3) with {Durability=17});
        f.Sql("INSERT INTO character_collectbox_state(character_id,selected_box_index) VALUES(1,1)");
        var before=await Assets(f);
        var ddl=native
            ? "CREATE TABLE collectbox_parent(id INTEGER PRIMARY KEY); CREATE TABLE collectbox_child(id INTEGER REFERENCES collectbox_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER collectbox_fault AFTER UPDATE OF selected_box_index ON character_collectbox_state BEGIN INSERT INTO collectbox_child VALUES(404); END;"
            : "CREATE TRIGGER collectbox_fault BEFORE INSERT ON character_collectbox_state BEGIN SELECT RAISE(ABORT,'collection box action SQL failure'); END;";
        f.Sql(ddl); byte[] body=[1,0,65,0,0xE8,3,0,0];var metadata=new GameMessageMetadata(1,0x03E6,0,7);
        var thrown=await Record.ExceptionAsync(()=>SendRaw(f,trace,metadata,body).AsTask());
        var cause=Assert.IsType<SqliteException>(thrown);Assert.Equal(19,cause.SqliteErrorCode);Assert.Equal(native?787:1811,cause.SqliteExtendedErrorCode);
        var attempt=Assert.Single(trace.Attempts);Assert.Equal(1,attempt.ForeignKeys);Assert.Equal(native?1:0,attempt.Children);
        Assert.Equal(native,attempt.Mutation is not null);if(native){Assert.Null(attempt.ActionFailure);SqliteFailureAssertions.FromCommit(attempt.Transaction, cause);}
        else {Assert.Same(cause,attempt.ActionFailure);SqliteFailureAssertions.FromAction(attempt.Transaction, cause);}
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,attempt.Transaction.Completion);
        var entry=attempt.Entry;var ordinal=entry.Ordinal;Assert.Same(entry,Entry(f));Assert.Equal(EffectOperationState.ConfirmedRollback,entry.State);
        Assert.Equal(native,entry.Projection is not null);Assert.Equal(before,await Assets(f));
        Assert.Empty(f.Sender.Batches);Assert.Empty(f.Sender.Messages);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        var close=Assert.Single(f.Sender.Outbound.Entries);Assert.True(close.IsClose);Assert.Same(f.Connection,close.Connection);
        var failure=new {task20OldSql=true,native,ddl,body=Convert.ToHexString(body),entry.Selector,entry.Type,entry.Sequence,entry.Length,entry.Hash,ordinal,
            before,stage=attempt.Stage,attempt.StagedBox,attempt.ForeignKeys,attempt.Children,mutation=attempt.Mutation,completion=attempt.Transaction.Completion.ToString(),
            state=entry.State.ToString(),cause=cause.ToString(),actionSame=ReferenceEquals(cause,attempt.ActionFailure),sourceCloseExact=true};
        f.Sql("DROP TRIGGER collectbox_fault");await SendRaw(f,trace,metadata,body);
        Assert.Same(entry,Entry(f));Assert.Equal(ordinal,entry.Ordinal);Assert.Equal(EffectOperationState.Committed,entry.State);
        Assert.Equal(2,f.Scalar("SELECT count FROM item_slots WHERE list_type=0 AND slot_index=65"));Assert.Equal(1,f.Scalar("SELECT count FROM item_slots WHERE list_type=241 AND slot_index=256"));
        var committed=await Assets(f);var first=Assert.Single(f.Sender.Batches);Assert.Equal(0x000E,BinaryPrimitives.ReadUInt16LittleEndian(first.Frames[0].AsSpan(1)));
        Assert.Equal(new byte[]{1},first.Frames[^1][15..]);Assert.Equal(5,first.Frames.Length);
        var firstFrames=first.Frames.Select(Convert.ToHexString).ToArray();var ackCount=f.Sender.Messages.Count(m=>m.MessageType==ServerMessageType.GameCollectionBox);
        await SendRaw(f,trace,metadata,body);Assert.Equal(2,trace.Attempts.Count);Assert.Equal(committed,await Assets(f));Assert.Equal(ordinal,entry.Ordinal);
        Assert.Equal(ackCount,f.Sender.Messages.Count(m=>m.MessageType==ServerMessageType.GameCollectionBox));Assert.All(f.Sender.Batches.Last().Frames,frame=>Assert.Equal(0,frame[0]));
        Assert.Single(f.Sender.Outbound.Closes);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        output.WriteLine(JsonSerializer.Serialize(new {failure,committed,firstFrames,replayFrames=f.Sender.Batches.Last().Frames.Select(Convert.ToHexString),sameEntry=true,ordinal}));
    }

    [Theory]
    [InlineData(0,0x5F)] [InlineData(1,0x5F)] [InlineData(2,0x01)] [InlineData(3,0x5F)]
    public async Task Four_actual_decoders_keep_named_domain_refusals_and_rollback_complete_assets(int action,int code)
    {
        var trace=new Trace();using var f=Fixture(trace);await f.Seed(65,1000,2);var before=await Assets(f);
        var box=action<2?(byte)9:(byte)1;
        byte[] body=action==0?[box]:action==2?[box,0xE8,3,0,0]:[box,0,65,0,0xE8,3,0,0];
        await SendRaw(f,trace,new(1,(ushort)(0x03E5+action),0,7),body);
        Assert.Equal(before,await Assets(f));Assert.Equal(new byte[]{0,(byte)code},Assert.Single(f.Sender.Outbound.Frames)[15..]);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,Assert.Single(trace.Attempts).Transaction.Completion);
        Assert.Equal(EffectOperationState.ConfirmedRollback,Entry(f).State);Assert.False(Assert.Single(trace.Results));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Initial_rejected_actor_cannot_upgrade_after_capture_or_claim_Main(bool late)
    {
        var trace=new Trace();using var f=Fixture(trace);await f.Seed(65,1000,2);
        var sessions=f.Services.GetRequiredService<ISelectedSessionDomainService>();TestGameplay.Run(f.Services,()=>sessions.ReleaseExact(f.Session));
        if(late)trace.BeforeWhole=()=>{f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(f.Session.CharacterId);f.Select(1);};
        await SendRaw(f,trace,new(1,0x03E5,0,7),[4]);
        Assert.IsType<CommandRequestContext.ProtocolEffectRejected>(Assert.Single(trace.Contexts).Request);
        Assert.Empty(trace.Attempts);Assert.False(Assert.Single(trace.Results));Assert.Empty(f.Sender.Messages);Assert.Equal(2,f.Count(1000));
    }

    [Fact]
    public async Task Successful_actual_Put_then_Withdraw_preserve_UID_full_core_box_clear_order_clock_and_current_replay()
    {
        var trace=new Trace{MonotonicClock=true};using var f=Fixture(trace);await f.SeedCore(121,StackableItemCore.Create(3,1000,1) with {Durability=17,EnchantCardId=123});
        var uid=f.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=121");var original=await Assets(f);
        await SendRaw(f,trace,new(1,0x03E6,0,7),[1,0,121,0,0xE8,3,0,0]);
        Assert.Equal(uid,f.Scalar("SELECT core_uid FROM item_slots WHERE list_type=241 AND slot_index=256"));Assert.Equal(0,f.Scalar("SELECT count(*) FROM item_slots WHERE list_type=0"));
        var put=Assert.Single(f.Sender.Batches);Assert.Equal(5,put.Frames.Length);Assert.Equal(0x000E,BinaryPrimitives.ReadUInt16LittleEndian(put.Frames[0].AsSpan(1)));Assert.Equal(new byte[]{1},put.Frames[^1][15..]);
        await SendRaw(f,trace,new(1,0x03E7,0,8),[1,0xE8,3,0,0]);
        Assert.Equal(0,f.Scalar("SELECT count(*) FROM item_slots WHERE list_type=241"));Assert.Equal(uid,f.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0"));
        var final=await Assets(f);Assert.Equal(Core(original),Core(final));Assert.Equal(2,trace.Attempts.Count);Assert.All(trace.Attempts,x=>Assert.Equal(DomainTransactionCompletion.Committed,x.Transaction.Completion));
        var withdraw=f.Sender.Batches.Last();Assert.Equal(5,withdraw.Frames.Length);Assert.Equal(0x000E,BinaryPrimitives.ReadUInt16LittleEndian(withdraw.Frames[0].AsSpan(1)));
        Assert.Equal(new byte[]{1,4,1},withdraw.Frames.Skip(1).Take(3).Select(frame=>frame[15]));Assert.All(withdraw.Frames.Skip(1).Take(3),frame=>Assert.Equal(0,frame[22]));Assert.Equal(new byte[]{1},withdraw.Frames[^1][15..]);
        Assert.Equal(2,trace.ClockValues.Count);Assert.NotEqual(trace.ClockValues[0],trace.ClockValues[1]);
        var entry=Entry(f.Services,f.Connection,8);var ordinal=entry.Ordinal;f.Sql("UPDATE character_collectbox_state SET selected_box_index=4 WHERE character_id=1");var current=await Assets(f);
        await SendRaw(f,trace,new(1,0x03E7,0,8),[1,0xE8,3,0,0]);Assert.Equal(2,trace.Attempts.Count);Assert.Equal(current,await Assets(f));Assert.Equal(ordinal,entry.Ordinal);
        Assert.All(f.Sender.Batches.Last().Frames,frame=>Assert.Equal(0,frame[0]));Assert.Equal(2,f.Sender.Messages.Count(m=>m.MessageType==ServerMessageType.GameCollectionBox));
        output.WriteLine(JsonSerializer.Serialize(new{task20PutWithdraw=true,uid,original,final,current,putFrames=put.Frames.Select(Convert.ToHexString),withdrawFrames=withdraw.Frames.Select(Convert.ToHexString),replay=f.Sender.Batches.Last().Frames.Select(Convert.ToHexString),trace.ClockValues,ordinal}));
    }
    [Fact]
    public async Task Successful_Withdraw_actual_encoder_failure_keeps_original_UID_committed_entry_and_current_only_retry()
    {
        var trace=new Trace();using var f=Fixture(trace);await f.SeedCore(121,StackableItemCore.Create(3,1000,1) with {Durability=17});
        var uid=f.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0");
        await SendRaw(f,trace,new(1,0x03E6,0,7),[1,0,121,0,0xE8,3,0,0]);
        var cause=new IOException("actual successful Withdraw encoder cause");var fault=true;var actualCalls=0;
        f.Sender.Outbound.Encoder.Fail((_,_)=>{actualCalls++;return fault?cause:null;});
        var error=await Record.ExceptionAsync(()=>SendRaw(f,trace,new(1,0x03E7,0,8),[1,0xE8,3,0,0]).AsTask());
        Assert.Same(cause,error);Assert.Equal(1,actualCalls);Assert.Equal(uid,f.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0"));Assert.Equal(0,f.Scalar("SELECT count(*) FROM item_slots WHERE list_type=241"));
        var entry=Entry(f.Services,f.Connection,8);var ordinal=entry.Ordinal;Assert.Equal(EffectOperationState.Committed,entry.State);Assert.Single(f.Sender.Batches);Assert.Single(f.Sender.Outbound.Closes);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));Assert.Same(f.Connection,f.Sender.Outbound.Entries.Last().Connection);
        await f.SeedCore(121,StackableItemCore.Create(3,1000,6) with {Durability=19});f.Sql("UPDATE character_collectbox_state SET selected_box_index=4 WHERE character_id=1");var current=await Assets(f);
        fault=false;await SendRaw(f,trace,new(1,0x03E7,0,8),[1,0xE8,3,0,0]);
        Assert.Equal(2,trace.Attempts.Count);Assert.Equal(current,await Assets(f));Assert.Same(entry,Entry(f.Services,f.Connection,8));Assert.Equal(ordinal,entry.Ordinal);
        Assert.All(f.Sender.Batches.Last().Frames,frame=>Assert.Equal(0,frame[0]));Assert.Single(f.Sender.Messages,m=>m.MessageType==ServerMessageType.GameCollectionBox);
        output.WriteLine(JsonSerializer.Serialize(new{task20WithdrawEncoder=true,uid,ordinal,state=entry.State.ToString(),causeSame=true,current,replay=f.Sender.Batches.Last().Frames.Select(Convert.ToHexString),sourceCloseExact=true}));
    }

    private static string Core(AssetState assets)=>JsonDocument.Parse(assets.Items).RootElement.EnumerateArray().Single().GetProperty("item_core").GetString()!;

    [Fact]
    public async Task Actual_success_batch_keeps_original_physical_target_when_the_real_callback_replaces_its_ID()
    {
        var trace=new Trace();using var f=Fixture(trace);await f.Seed(65,1000,2);var replacement=f.Connection with {};var hooks=0;
        f.Sender.BeforeSend=()=>{hooks++;f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId);f.Sender.Outbound.Connections.Add(replacement);};
        await SendRaw(f,trace,new(1,0x03E6,0,7),[1,0,65,0,0xE8,3,0,0]);
        Assert.Equal(1,hooks);Assert.NotSame(f.Connection,replacement);Assert.True(f.Sender.Outbound.Connections.TryGet(f.Connection.ConnectionId,out var current));Assert.Same(replacement,current);
        Assert.All(f.Sender.Outbound.Entries,entry=>Assert.Same(f.Connection,entry.Connection));Assert.DoesNotContain(f.Sender.Outbound.Entries,entry=>ReferenceEquals(entry.Connection,replacement));Assert.Equal(EffectOperationState.Committed,Entry(f).State);
    }

    [Theory]
    [InlineData("callback")] [InlineData("clock")] [InlineData("mapping")] [InlineData("full")] [InlineData("current")]
    public async Task Nonencoder_failure_after_actual_Main_uses_default_typed_fence_and_keeps_its_cause(string kind)
    {
        var trace=new Trace{FaultKind=kind};using var f=Fixture(trace);await f.Seed(65,1000,3);var encoderCalls=0;var callbacks=0;
        f.Sender.Outbound.Encoder.Fail((_,_)=>{encoderCalls++;return null;});
        f.Sender.BeforeSend=()=>{callbacks++;if(kind=="callback")throw trace.Cause;if(kind=="current")f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session);};
        var error=await Record.ExceptionAsync(()=>SendRaw(f,trace,new(1,0x03E6,0,7),[1,0,65,0,0xE8,3,0,0]).AsTask());
        output.WriteLine(JsonSerializer.Serialize(new{task20DefaultSource=true,kind,encoderCalls,callbacks,committed=Entry(f).State.ToString(),blocked=f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection),causeSame=error is not null&&Contains(error,trace.Cause),error=error?.ToString()}));
        Assert.NotNull(error);Assert.IsType<TransactionOutcomeUncertainException>(error);Assert.Equal(2,f.Scalar("SELECT count FROM item_slots WHERE list_type=0 AND slot_index=65"));
        Assert.Equal(EffectOperationState.Committed,Entry(f).State);Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.True(f.Sender.Outbound.Entries.Last().IsClose);Assert.Same(f.Connection,f.Sender.Outbound.Entries.Last().Connection);
        if(kind is "callback" or "clock" or "mapping")Assert.True(Contains(error,trace.Cause));
        Assert.Equal(0,encoderCalls);
        Assert.Equal(kind is "callback" or "current"?1:0,callbacks);
    }
    private static bool Contains(Exception error,Exception cause)=>ReferenceEquals(error,cause)||error is AggregateException aggregate&&aggregate.InnerExceptions.Any(x=>Contains(x,cause))||error.InnerException is {} inner&&Contains(inner,cause);

    private static ShopFixture Fixture(Trace trace)=>new(services=>{
        services.AddSingleton<ICollectionBoxDefinitionSource,Definitions>();
        services.AddSingleton(trace);
        var clock=(TimeProvider)services.Last(x=>x.ServiceType==typeof(TimeProvider)).ImplementationInstance!;
        services.AddSingleton<TimeProvider>(new ObservedClock(clock,trace));
        services.AddSingleton<IInventorySlotMappingRuleService>(new ObservedMapping(trace));
        services.AddScoped<GameCollectionBoxCommandHandler>();services.AddScoped<ICommandHandler<GameCollectionBoxCommand,bool>,ObserveWhole>();
        services.AddSingleton<CollectionBoxDomainService>();
        services.AddSingleton<ICollectionBoxDomainService>(p=>new Observe(p.GetRequiredService<CollectionBoxDomainService>(),p,trace));
    });
    private sealed class Definitions:ICollectionBoxDefinitionSource
    {
        private static readonly CollectionBoxDefinition[] boxes=[new(1,1,1,null,null,[new(0,0,1000,1)],[]),new(4,1,1,null,null,[new(0,0,1000,1)],[])];
        public ValueTask<IReadOnlyList<CollectionBoxDefinition>> GetAllAsync(CancellationToken ct)=>ValueTask.FromResult<IReadOnlyList<CollectionBoxDefinition>>(boxes);
        public ValueTask<CollectionBoxDefinition?> GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult(boxes.SingleOrDefault(x=>x.BoxIndex==id));
    }
    private sealed class Trace
    {
        internal readonly GameInboundFrameCounter Counter=new();
        internal readonly List<CollectionBoxRequest> Inputs=[];
        internal readonly List<Attempt> Attempts=[];
        internal int Reads;
        internal string? FaultKind;
        internal bool InWhole,MonotonicClock;
        internal readonly IOException Cause=new("same CollectionBox business or callback IO failure");
        internal Action? BeforeWhole;
        internal readonly List<CommandExecutionContext> Contexts=[];
        internal readonly List<bool> Results=[];
        internal readonly List<DateTimeOffset> ClockValues=[];
        internal bool Native,ObserveSql;
        internal NetworkConnectionContext? Connection;
    }
    private sealed class ObserveWhole(GameCollectionBoxCommandHandler actual,Trace trace):ICommandHandler<GameCollectionBoxCommand,bool>
    {
        public async ValueTask<bool> HandleAsync(CommandExecutionContext context,GameCollectionBoxCommand command,CancellationToken ct)
        {trace.Contexts.Add(context);trace.BeforeWhole?.Invoke();trace.InWhole=true;try{var result=await actual.HandleAsync(context,command,ct);trace.Results.Add(result);return result;}finally{trace.InWhole=false;}}
    }
    private sealed class ObservedClock(TimeProvider actual,Trace trace):TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            if(trace.InWhole&&trace.Attempts.LastOrDefault()?.Transaction.Completion==DomainTransactionCompletion.Committed)
            {
                if(trace.FaultKind=="clock")throw trace.Cause;
                var at=actual.GetUtcNow().AddSeconds(trace.MonotonicClock?trace.ClockValues.Count:0);trace.ClockValues.Add(at);return at;
            }
            return actual.GetUtcNow();
        }
    }
    private sealed class ObservedMapping(Trace trace):IInventorySlotMappingRuleService
    {
        private readonly InventorySlotMappingRuleService actual=new();
        public BusinessSlot ToBusinessSlot(GameSlot source)=>actual.ToBusinessSlot(source);
        public GameSlot ToGameSlot(BusinessSlot source)
        {if(trace.FaultKind=="mapping"&&trace.Attempts.LastOrDefault()?.Transaction.Completion==DomainTransactionCompletion.Committed)throw trace.Cause;return actual.ToGameSlot(source);}
    }
    private sealed record Attempt(EffectOperationEntry Entry,DomainTransaction Transaction)
    {
        internal CollectionBoxMutation? Mutation;
        internal Exception? ActionFailure;
        internal AssetState? Stage;
        internal string? StagedBox;
        internal int ForeignKeys,Children;
    }
    private sealed class Observe(ICollectionBoxDomainService actual,IServiceProvider services,Trace trace):ICollectionBoxDomainService
    {
        public ValueTask<CollectionBoxSnapshot> ReadAsync(CharacterId character,CancellationToken ct)
        {trace.Reads++;return actual.ReadAsync(character,ct);}
        public async ValueTask<CollectionBoxMutation> ExecuteAsync(AccountId account,CharacterId character,CollectionBoxAction action,byte box,BusinessSlot? source,int itemId,CancellationToken ct)
        {
            var entry=Entry(services,trace.Connection!,trace.Inputs.Last().Metadata.Sequence);
            var tx=Assert.IsType<DomainTransaction>(services.GetRequiredService<IDomainTransactionAccessor>().Current);
            var attempt=new Attempt(entry,tx);trace.Attempts.Add(attempt);
            try
            {
                var mutation=await actual.ExecuteAsync(account,character,action,box,source,itemId,ct);
                if(trace.FaultKind=="full")mutation=mutation with {State=mutation.State with {SelectedBoxId=99}};
                return attempt.Mutation=mutation;
            }
            catch(Exception error){attempt.ActionFailure=error;throw;}
            finally
            {
                if(trace.ObserveSql)
                {
                    attempt.ForeignKeys=await Scalar(services,"PRAGMA foreign_keys",ct);
                    attempt.Children=trace.Native?await Scalar(services,"SELECT count(*) FROM collectbox_child",ct):0;
                    attempt.Stage=await AssetsIn(services,ct);
                    var held=await services.GetRequiredService<IItemDomainStore>().SessionIn(tx).GetAllAsync(ContainerId.ForCharacter(character,InventoryContainerType.CollectionBox),ct);
                    attempt.StagedBox=JsonSerializer.Serialize(held.Select(x=>new {x.ItemUid,x.ItemId,x.Count,x.StorageSlotIndex,x.Snapshot}));
                }
            }
        }
    }
    private static ValueTask SendRaw(ShopFixture f,Trace trace,GameMessageMetadata input,byte[] body)
    {
        trace.Connection=f.Connection;
        var(ordinal,fresh)=trace.Counter.Observe(input.Sequence);
        var metadata=(input with {InboundOrdinal=ordinal,SequenceIsFresh=fresh}).WithRawBody(body);
        var registration=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==metadata.Selector&&x.Type==metadata.Type);
        var decoded=Assert.IsType<CollectionBoxRequest>(((IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType)).Decode(metadata,body));
        trace.Inputs.Add(decoded);
        return f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),decoded,default);
    }
    private sealed record AssetState(string Items,string Slots,string Selection,string Cooldowns,string Effects,string Events,string Deliveries);
    private static Task<AssetState> Assets(ShopFixture f)=>TestGameplay.RunAsync(f.Services,async()=>await AssetsIn(f.Services,default));
    private static async ValueTask<AssetState> AssetsIn(IServiceProvider services,CancellationToken ct)=>new AssetState(
        await Rows(services,"SELECT * FROM items ORDER BY item_uid",ct),await Rows(services,"SELECT * FROM item_slots ORDER BY owner_scope,owner_id,list_type,slot_index",ct),
        await Rows(services,"SELECT * FROM character_collectbox_state ORDER BY character_id",ct),await Rows(services,"SELECT * FROM character_item_cooldowns ORDER BY sort_order",ct),
        await Rows(services,"SELECT * FROM character_item_effects ORDER BY sort_order",ct),await Rows(services,"SELECT * FROM domain_events ORDER BY event_id",ct),
        await Rows(services,"SELECT * FROM domain_event_deliveries",ct));
    private static ValueTask<string> Rows(IServiceProvider services,string sql,CancellationToken ct)=>services.GetRequiredService<SqliteDatabase>().ReadAsync(async(cmd,token)=>{
        cmd.CommandText=sql;await using var reader=await cmd.ExecuteReaderAsync(token);var rows=new List<object>();
        while(await reader.ReadAsync(token))rows.Add(Enumerable.Range(0,reader.FieldCount).ToDictionary(i=>reader.GetName(i),i=>reader.IsDBNull(i)?null:reader.GetValue(i) is byte[] blob?Convert.ToHexString(blob):Convert.ToString(reader.GetValue(i))));
        return JsonSerializer.Serialize(rows);
    },ct);
    private static ValueTask<int> Scalar(IServiceProvider services,string sql,CancellationToken ct)=>services.GetRequiredService<SqliteDatabase>().ReadAsync(async(cmd,token)=>{cmd.CommandText=sql;return Convert.ToInt32(await cmd.ExecuteScalarAsync(token));},ct);
    private static EffectOperationEntry Entry(ShopFixture f)=>Entry(f.Services,f.Connection);
    private static EffectOperationEntry Entry(IServiceProvider services,NetworkConnectionContext connection,ushort? sequence=null)
    {
        var ledger=services.GetRequiredService<EffectOperationLedger>();var table=typeof(EffectOperationLedger).GetField("ledgers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ledger)!;
        object?[] args=[connection,null];Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,args)!);
        var entries=(IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!;return sequence is {} number?Assert.Single(entries.Values.Cast<EffectOperationEntry>(),x=>x.Sequence==number):Assert.Single(entries.Values.Cast<EffectOperationEntry>());
    }
}
