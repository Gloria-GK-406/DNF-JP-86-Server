using Dfo.Server.Application.Tests.Support;
using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
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
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Protocol.Game.InventorySort;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class InventorySortProtocolCommandFlowTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Actual_level_wire_keeps_UIDs_full_core_locked_slot_and_one_ordered_batch_with_Loads_in_the_write()
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f);
        var before = await Assets(f); var low = f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=12");
        var high = f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=10"); var other = f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=14");
        var locked = f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=11");
        await InventorySortWire.Send(f, 0x0014, [0,1,2], 7, trace.Inputs);
        output.WriteLine(JsonSerializer.Serialize(new { task21LevelActual=true, assets=await Assets(f),
            actualBySlot=new[]{9,10,11,12}.Select(slot=>new{slot,uid=f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index="+slot)}),low,high,other,locked }));
        Assert.Equal(high, f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=9"));
        Assert.Equal(low, f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=10"));
        Assert.Equal(locked, f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=11"));
        Assert.Equal(other, f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=12"));
        Assert.Equal(ItemsCore(before), ItemsCore(await Assets(f))); Assert.Equal(100000, f.Gold);
        var attempt = Assert.Single(trace.Attempts); Assert.True(attempt.SortCompleted);
        Assert.Equal(DomainTransactionCompletion.Committed, attempt.Transaction.Completion);
        Assert.Equal(new[]{"sort", "character.load", "account.load", "clock"}, attempt.Steps);
        Assert.All(attempt.LoadTransactions, tx => Assert.Same(attempt.Transaction, tx));
        var batch = Assert.Single(f.Sender.Batches); Assert.Same(f.Connection, batch.Connection);
        Assert.Equal(new ushort[]{0x0014,0x000D,0x00FB}, batch.Frames.Select(Type));
        Assert.Equal(new byte[]{1,0}, batch.Frames[0][15..]); AssertNoLedger(f);
        output.WriteLine(JsonSerializer.Serialize(new { task21Level=true,before,after=await Assets(f),low,high,other,locked,
            inputs=trace.Inputs,steps=attempt.Steps,completion=attempt.Transaction.Completion.ToString(),frames=batch.Frames.Select(Convert.ToHexString),exactPhysical=true }));
    }

    [Fact]
    public async Task Actual_Toggle_Toggle_Unlock_Unlock_preserves_core_and_four_ACKs_even_when_sequence_repeats()
    {
        var trace = new Trace(); using var f = Fixture(trace);
        await f.SeedCore(28, StackableItemCore.Create(1,3000,77) with {Durability=8,EnchantCardId=42}); var original=f.StoredCore(28);
        foreach(var type in new ushort[]{0x02CA,0x02CA,0x02CB,0x02CB})
            await InventorySortWire.Send(f,type,[0,28,0],7,trace.Inputs);
        Assert.Equal(original,f.StoredCore(28)); Assert.Equal(4,f.Sender.Batches.Count);
        Assert.Equal(new ushort[]{0x02CA,0x02CB,0x02CB,0x02CB},f.Sender.Outbound.Frames.Select(Type));
        Assert.All(f.Sender.Outbound.Frames,frame=>Assert.Equal(new byte[]{1,0,28,0},frame[15..]));
        Assert.Equal(new[]{true,false,false,false},trace.Inputs.Select(x=>x.SequenceIsFresh)); AssertNoLedger(f);
        Assert.Equal(4,trace.Attempts.Count); Assert.All(trace.Attempts,x=>Assert.Equal(DomainTransactionCompletion.Committed,x.Transaction.Completion));
        output.WriteLine(JsonSerializer.Serialize(new{task21Locks=true,inputs=trace.Inputs,original=Convert.ToHexString(original),final=Convert.ToHexString(f.StoredCore(28)),frames=f.Sender.Outbound.Frames.Select(Convert.ToHexString),attempts=trace.Attempts.Count}));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_sort_SQL_action_and_native_Commit_failures_keep_cause_full_rollback_then_same_sequence_reexecutes(bool native)
    {
        var trace=new Trace{Native=native,ObserveSql=true};using var f=Fixture(trace);await Seed(f);var before=await Assets(f);
        var ddl=native
            ? "CREATE TABLE sort_parent(id INTEGER PRIMARY KEY); CREATE TABLE sort_child(id INTEGER REFERENCES sort_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER sort_fault AFTER INSERT ON item_slots WHEN NEW.owner_scope='character' AND NEW.owner_id=1 AND NEW.list_type=0 BEGIN INSERT INTO sort_child VALUES(404); END;"
            : "CREATE TRIGGER sort_fault BEFORE INSERT ON item_slots WHEN NEW.owner_scope='character' AND NEW.owner_id=1 AND NEW.list_type=0 BEGIN SELECT RAISE(ABORT,'sort action SQL failure'); END;";
        f.Sql(ddl); var thrown=await Record.ExceptionAsync(()=>InventorySortWire.Send(f,0x0014,[0,1,2],7,trace.Inputs).AsTask());
        var cause=Assert.IsType<SqliteException>(thrown);Assert.Equal(19,cause.SqliteErrorCode);Assert.Equal(native?787:1811,cause.SqliteExtendedErrorCode);
        var attempt=Assert.Single(trace.Attempts);Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,attempt.Transaction.Completion);
        Assert.Equal(1,attempt.ForeignKeys);Assert.Equal(native,attempt.SortCompleted);Assert.Equal(before,await Assets(f));
        if(native){Assert.True(attempt.Children>0);SqliteFailureAssertions.FromCommit(attempt.Transaction, cause);Assert.Same(cause,attempt.Transaction.CommitFailure!.Cause);Assert.Equal(new[]{"sort","character.load","account.load","clock"},attempt.Steps);}
        else{Assert.Same(cause,attempt.ActionFailure);SqliteFailureAssertions.FromAction(attempt.Transaction, cause);}
        Assert.Empty(f.Sender.Batches);Assert.Empty(f.Sender.Messages);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        var close=Assert.Single(f.Sender.Outbound.Entries);Assert.True(close.IsClose);Assert.Same(f.Connection,close.Connection);AssertNoLedger(f);
        var fault=new{native,ddl,before,stage=attempt.Stage,attempt.ForeignKeys,attempt.Children,attempt.SortCompleted,steps=attempt.Steps,
            completion=attempt.Transaction.Completion.ToString(),cause=cause.ToString(),causeSame=true,inputs=trace.Inputs.ToArray(),sourceCloseExact=true,blocked=false};
        f.Sql("DROP TRIGGER sort_fault");await InventorySortWire.Send(f,0x0014,[0,1,2],7,trace.Inputs);
        Assert.Equal(2,trace.Attempts.Count);Assert.Equal(DomainTransactionCompletion.Committed,trace.Attempts[1].Transaction.Completion);
        Assert.Equal(new ushort[]{0x0014,0x000D,0x00FB},Assert.Single(f.Sender.Batches).Frames.Select(Type));
        Assert.False(trace.Inputs.Last().SequenceIsFresh);AssertNoLedger(f);
        output.WriteLine(JsonSerializer.Serialize(new{task21SQL=true,fault,afterRetry=await Assets(f),retryInput=trace.Inputs.Last(),frames=f.Sender.Batches.Single().Frames.Select(Convert.ToHexString)}));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Successful_projection_actual_encoder_failure_stays_Warning_Failed_with_committed_business_and_no_source_close(bool locking)
    {
        var trace=new Trace();using var f=Fixture(trace);await Seed(f);var calls=0;
        f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);calls++;return trace.Cause;});
        var error=await Record.ExceptionAsync(()=>InventorySortWire.Send(f,locking?(ushort)0x02CA:(ushort)0x0014,locking?[0,12,0]:[0,1,2],7,trace.Inputs).AsTask());
        Assert.Null(error);Assert.Equal(1,calls);Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Attempts).Transaction.Completion);
        Assert.Empty(f.Sender.Batches);Assert.Empty(f.Sender.Messages);Assert.Empty(f.Sender.Outbound.Closes);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Single(trace.Logs,x=>x.Level==LogLevel.Warning&&ReferenceEquals(x.Error,trace.Cause));
        if(locking)Assert.Equal((byte)1,InventoryItemCore.Decode(f.StoredCore(12)).SortLockFlag);
        else Assert.Equal(100310669,InventoryItemCore.Decode(f.StoredCore(9)).ItemId);
        var assets=await Assets(f);AssertNoLedger(f);
        output.WriteLine(JsonSerializer.Serialize(new{task21ProjectionEncoder=true,locking,cause=trace.Cause.ToString(),causeSame=true,calls,completion=trace.Attempts[0].Transaction.Completion.ToString(),assets,inputs=trace.Inputs,closes=0,blocked=false,warningCount=trace.Logs.Count(x=>x.Level==LogLevel.Warning&&ReferenceEquals(x.Error,trace.Cause))}));
    }

    [Fact]
    public async Task Successful_projection_composer_failure_is_isolated_but_direct_prevalidation_ACK_failure_is_raw()
    {
        var trace=new Trace{ComposerFault=true};using var f=Fixture(trace);await Seed(f);
        var error=await Record.ExceptionAsync(()=>InventorySortWire.Send(f,0x0014,[0,1,2],7,trace.Inputs).AsTask());
        Assert.Null(error);Assert.Empty(f.Sender.Batches);Assert.Empty(f.Sender.Outbound.Closes);
        Assert.Single(trace.Logs,x=>x.Level==LogLevel.Warning&&ReferenceEquals(x.Error,trace.Cause));
        Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(trace.Attempts).Transaction.Completion);
        trace.ComposerFault=false;var committed=await Assets(f);var calls=0;f.Sender.Outbound.Encoder.Fail((_,_)=>{calls++;return trace.Cause;});
        var direct=await Record.ExceptionAsync(()=>InventorySortWire.Send(f,0x0014,[0,5,0],8,trace.Inputs).AsTask());
        Assert.Same(trace.Cause,direct);Assert.Equal(1,calls);Assert.Equal(committed,await Assets(f));Assert.Single(trace.Attempts);
        Assert.True(Assert.Single(f.Sender.Outbound.Entries).IsClose);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));AssertNoLedger(f);
        output.WriteLine(JsonSerializer.Serialize(new{task21ComposerAndDirect=true,committed,cause=trace.Cause.ToString(),causeSame=true,inputs=trace.Inputs,closes=1,blocked=false}));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_wire_using_captured_stale_query_keeps_normal_refusal_or_silence_and_old_assets(bool locking)
    {
        var trace=new Trace();using var f=Fixture(trace);await f.SeedCore(12,StackableItemCore.Create(1,3000,55));
        var old=f.Session;f.Select(2);trace.Stale=old;var before=await Assets(f);
        await InventorySortWire.Send(f,locking?(ushort)0x02CA:(ushort)0x0014,locking?[0,12,0]:[0,1,0],7,trace.Inputs);
        Assert.Equal(before,await Assets(f));Assert.Empty(trace.Attempts);Assert.Empty(f.Sender.Outbound.Closes);
        if(locking)Assert.Empty(f.Sender.Messages);else Assert.Equal(new byte[]{0,1,0},Assert.Single(f.Sender.Outbound.Frames)[15..]);
        AssertNoLedger(f);output.WriteLine(JsonSerializer.Serialize(new{task21Stale=true,locking,before,after=await Assets(f),inputs=trace.Inputs,frames=f.Sender.Outbound.Frames.Select(Convert.ToHexString),scope="test-only read-query proxy returns actual old session; world checks still use actual selected registry"}));
    }

    [Fact]
    public async Task Refusal_ACK_after_the_original_empty_committed_transaction_retains_the_failure_cause_and_assets()
    {
        var trace=new Trace();using var f=Fixture(trace);await f.SeedCore(12,StackableItemCore.Create(1,3000,55));
        var old=f.Session;f.Select(2);trace.Stale=old;var before=await Assets(f);var calls=0;
        f.Sender.Outbound.Encoder.Fail((_,_)=>{calls++;return trace.Cause;});
        var error=await Record.ExceptionAsync(()=>InventorySortWire.Send(f,0x0014,[0,1,0],7,trace.Inputs).AsTask());
        Assert.NotNull(trace.GuardTransaction);Assert.Equal(DomainTransactionCompletion.Committed,trace.GuardTransaction.Completion);
        var typed = Assert.IsType<TransactionOutcomeUncertainException>(error);
        Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure, typed.Reason);
        Assert.Same(trace.Cause, typed.InnerException); Assert.True(GameplayJob.EndedAJob(typed));
        Assert.Equal(before,await Assets(f));Assert.Equal(1,calls);Assert.Empty(f.Sender.Batches);
        var context = Assert.Single(trace.Contexts); var plain = Assert.IsType<CommandRequestContext.ProtocolNonEffect>(context.Request);
        Assert.True(plain.IsClosed); Assert.True(plain.HasCommittedUnit); Assert.Empty(plain.CommittedParticipants);
        Assert.Empty((System.Collections.ICollection)typeof(CommandRequestContext).GetField("attempts",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plain)!);
        var log = Assert.Single(trace.Logs, x => x.Level == LogLevel.Error && ReferenceEquals(x.Error, typed));
        Assert.NotNull(trace.GuardChain); var chain = trace.GuardChain.Value;
        Assert.True(chain.IsInitialized); Assert.Equal(chain.RootId, log.Fields["RootId"]);
        Assert.Equal(chain.ParentId, log.Fields["ParentId"]); Assert.Equal(chain.Depth, log.Fields["Depth"]);
        Assert.Equal(chain.Source, log.Fields["Source"]); Assert.Equal(chain.SourceConnection, log.Fields["SourceConnection"]);
        Assert.Equal(nameof(GameSortItemCommand), log.Fields["CommandType"]); Assert.Equal(GameplayCallSource.Connection, chain.Source);
        Assert.Equal(f.Connection.ConnectionId, chain.SourceConnection); AssertNoLedger(f);
        var close = Assert.Single(f.Sender.Outbound.Entries);Assert.True(close.IsClose);Assert.Same(f.Connection,close.Connection);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        output.WriteLine(JsonSerializer.Serialize(new{task21EmptyCommittedRefusal=true,before,after=await Assets(f),cause=error!.ToString(),causeSame=ReferenceEquals(typed.InnerException,trace.Cause),typed=true,reason=typed.Reason.ToString(),endedAJob=GameplayJob.EndedAJob(typed),chain=trace.GuardChain,completion=trace.GuardTransaction.Completion.ToString(),calls,inputs=trace.Inputs,sourceCloseExact=ReferenceEquals(f.Connection,close.Connection),blocked=false}));
    }

    [Fact]
    public async Task NonEffect_same_sequence_sort_executes_again_after_world_positions_change()
    {
        var trace=new Trace();using var f=Fixture(trace);await Seed(f);
        await InventorySortWire.Send(f,0x0014,[0,1,2],7,trace.Inputs);var high=f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=9");
        f.Sql("UPDATE item_slots SET slot_index=15 WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9");
        await InventorySortWire.Send(f,0x0014,[0,1,2],7,trace.Inputs);
        Assert.Equal(high,f.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=9"));Assert.Equal(2,trace.Attempts.Count);Assert.Equal(2,f.Sender.Batches.Count);
        Assert.Equal(new[]{true,false},trace.Inputs.Select(x=>x.SequenceIsFresh));AssertNoLedger(f);
    }

    [Theory]
    [InlineData("noActor")] [InlineData("notWorld")] [InlineData("noOnline")] [InlineData("changedOnline")]
    public async Task Real_wire_preconditions_keep_refusal_or_silence_without_assets_changing(string kind)
    {
        var trace=new Trace();using var f=Fixture(trace);await f.SeedCore(12,StackableItemCore.Create(1,3000,55));
        var selected=f.Services.GetRequiredService<ISelectedSessionDomainService>();
        if(kind is "noActor" or "notWorld")TestGameplay.Run(f.Services,()=>{selected.ReleaseExact(f.Session);if(kind=="notWorld")selected.Select(f.Session);});
        trace.OnlineMode=kind;trace.OnlineReads=0;var before=await Assets(f);
        await InventorySortWire.Send(f,0x0014,[0,1,0],7,trace.Inputs);
        Assert.Equal(new byte[]{0,1,0},Assert.Single(f.Sender.Outbound.Frames)[15..]);
        trace.OnlineReads=0;await InventorySortWire.Send(f,0x02CA,[0,12,0],8,trace.Inputs);
        Assert.Single(f.Sender.Outbound.Frames);Assert.Equal(before,await Assets(f));Assert.Empty(trace.Attempts);Assert.Empty(f.Sender.Outbound.Closes);AssertNoLedger(f);
    }

    [Fact]
    public async Task Valid_material_page_with_unsupported_condition_keeps_its_default_order_and_success_projection()
    {
        var trace=new Trace();using var f=Fixture(trace);await f.Seed(125,1000,6,3);await f.Seed(128,1000,7,3);
        var before=await Assets(f);await InventorySortWire.Send(f,0x0014,[0,3,2],7,trace.Inputs);
        Assert.Equal(6,f.Scalar("SELECT count FROM item_slots WHERE slot_index=121"));Assert.Equal(7,f.Scalar("SELECT count FROM item_slots WHERE slot_index=122"));
        Assert.Equal(ItemsCore(before),ItemsCore(await Assets(f)));Assert.Equal(new ushort[]{0x0014,0x000D,0x00FB},Assert.Single(f.Sender.Batches).Frames.Select(Type));
        Assert.Equal(new byte[]{1,0},f.Sender.Batches.Single().Frames[0][15..]);AssertNoLedger(f);
    }

    private static async Task Seed(ShopFixture f)
    {
        await f.SeedCore(12,StackableItemCore.Create(1,3000,55) with{Durability=4,EnchantCardId=42});
        await f.SeedCore(10,StackableItemCore.Create(1,100310669,77) with{Durability=8});
        await f.SeedCore(14,StackableItemCore.Create(1,3000,99) with{Durability=7});
        await f.SeedCore(11,StackableItemCore.Create(1,3000,111) with{SortLockFlag=1});
    }
    private static ushort Type(byte[] frame)=>BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
    private static string ItemsCore(AssetState assets)=>JsonSerializer.Serialize(JsonSerializer.Deserialize<List<Dictionary<string,string?>>>(assets.Items)!.Select(row=>row.Where(x=>x.Key is not("updated_at" or "created_at")).ToDictionary(x=>x.Key,x=>x.Value)));
    private static void AssertNoLedger(ShopFixture f)
    {
        var table=typeof(Dfo.Server.Application.Effects.EffectOperationLedger).GetField("ledgers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Services.GetRequiredService<Dfo.Server.Application.Effects.EffectOperationLedger>())!;
        object?[] arguments=[f.Connection,null];Assert.False((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,arguments)!);
    }
    internal static ShopFixture CreateFixture(Trace trace)=>Fixture(trace);
    private static ShopFixture Fixture(Trace trace)=>new(services=>{
        services.AddSingleton<IInventorySortKeySource,Keys>();services.AddSingleton<IItemPlacementDefinitionSource,Placements>();
        services.AddSingleton<InventorySortDomainService>();services.AddSingleton<IInventorySortDomainService>(p=>new ObserveSorting(p.GetRequiredService<InventorySortDomainService>(),p,trace));
        services.AddSingleton<CharacterInventoryEntryDomainService>();services.AddSingleton<ICharacterInventoryEntryDomainService>(p=>new ObserveCharacter(p.GetRequiredService<CharacterInventoryEntryDomainService>(),p,trace));
        services.AddSingleton<AccountInventoryEntryDomainService>();services.AddSingleton<IAccountInventoryEntryDomainService>(p=>new ObserveAccount(p.GetRequiredService<AccountInventoryEntryDomainService>(),p,trace));
        var clock=(TimeProvider)services.Last(x=>x.ServiceType==typeof(TimeProvider)).ImplementationInstance!;services.AddSingleton<TimeProvider>(p=>new ObserveClock(clock,p,trace));
        var selected=services.Last(x=>x.ServiceType==typeof(ISelectedSessionDomainService)).ImplementationFactory!;
        services.AddSingleton<ISelectedSessionDomainService>(p=>QueryProxy.Create((ISelectedSessionDomainService)selected(p),p,trace));
        var users=services.Last(x=>x.ServiceType==typeof(IOnlineUserQueryDomainService)).ImplementationFactory!;
        services.AddSingleton<IOnlineUserQueryDomainService>(p=>OnlineProxy.Create((IOnlineUserQueryDomainService)users(p),trace));
        services.AddScoped<CommandWorkUnits>();services.AddScoped<ICommandWorkUnits>(p=>new ObserveUnits(p.GetRequiredService<CommandWorkUnits>(),trace));
        services.AddLogging(log=>log.AddProvider(new Logs(trace)));
    });
    internal sealed class Trace
    {
        internal readonly List<WireObservation> Inputs=[];internal readonly List<Attempt> Attempts=[];internal readonly List<LogRow> Logs=[];
        internal bool ObserveSql,Native,ComposerFault,AwaitFinalClock;internal SelectedSessionIdentity? Stale;internal readonly IOException Cause=new("fixed sort projection or direct ACK cause");
        internal Attempt? Active;internal DomainTransaction? GuardTransaction;internal GameplayCallChain? GuardChain;internal readonly List<CommandExecutionContext> Contexts=[];internal readonly List<CommandWorkUnitDeclaration> Declarations=[];internal string? OnlineMode;internal int OnlineReads;
    }
    internal sealed record Attempt(DomainTransaction Transaction)
    {
        internal readonly List<string> Steps=[];internal readonly List<IDomainTransaction> LoadTransactions=[];
        internal bool SortCompleted;internal Exception? ActionFailure;internal AssetState? Stage;internal int ForeignKeys,Children;
    }
    private sealed class ObserveSorting(IInventorySortDomainService actual,IServiceProvider services,Trace trace):IInventorySortDomainService
    {
        private Attempt Begin(){var tx=Assert.IsType<DomainTransaction>(services.GetRequiredService<IDomainTransactionAccessor>().Current);Assert.False(tx.IsReadOnly);Assert.False(tx.IsSettled);var attempt=new Attempt(tx);trace.Attempts.Add(attempt);trace.Active=attempt;return attempt;}
        public async ValueTask<bool> SortAsync(CharacterId character,BusinessInventorySpace space,InventorySortOrder order,CancellationToken ct)
        {
            var attempt=Begin();attempt.Steps.Add("sort");
            try{var result=await actual.SortAsync(character,space,order,ct);attempt.SortCompleted=true;return result;}
            catch(Exception error){attempt.ActionFailure=error;throw;}
            finally{if(trace.ObserveSql&&!attempt.SortCompleted)await Stage(services,trace,attempt,ct);}
        }
        public async ValueTask<bool?> ChangeLockAsync(CharacterId character,BusinessSlot slot,InventorySortLockAction action,CancellationToken ct)
        {var attempt=Begin();attempt.Steps.Add("lock");try{var result=await actual.ChangeLockAsync(character,slot,action,ct);attempt.SortCompleted=true;return result;}catch(Exception error){attempt.ActionFailure=error;throw;}}
    }
    private sealed class ObserveCharacter(ICharacterInventoryEntryDomainService actual,IServiceProvider services,Trace trace):ICharacterInventoryEntryDomainService
    {
        public ValueTask<CharacterInventoryEntryState> ReadCurrentAsync(AccountId account,CharacterId character,CancellationToken ct)=>actual.ReadCurrentAsync(account,character,ct);
        public async ValueTask<CharacterInventoryEntryState> LoadAsync(AccountId account,CharacterId character,CancellationToken ct)
        {var attempt=trace.Active!;var tx=services.GetRequiredService<IDomainTransactionAccessor>().Current!;Assert.Same(attempt.Transaction,tx);Assert.False(attempt.Transaction.IsSettled);attempt.Steps.Add("character.load");attempt.LoadTransactions.Add(tx);return await actual.LoadAsync(account,character,ct);}
    }
    private sealed class ObserveAccount(IAccountInventoryEntryDomainService actual,IServiceProvider services,Trace trace):IAccountInventoryEntryDomainService
    {
        public async ValueTask<AccountInventoryEntryState> LoadAsync(AccountId account,CharacterId character,CancellationToken ct)
        {var attempt=trace.Active!;var tx=services.GetRequiredService<IDomainTransactionAccessor>().Current!;Assert.Same(attempt.Transaction,tx);Assert.False(attempt.Transaction.IsSettled);attempt.Steps.Add("account.load");attempt.LoadTransactions.Add(tx);var result=await actual.LoadAsync(account,character,ct);if(trace.ObserveSql)await Stage(services,trace,attempt,ct);trace.AwaitFinalClock=true;return result;}
    }
    private sealed class ObserveClock(TimeProvider actual,IServiceProvider services,Trace trace):TimeProvider
    {
        public override DateTimeOffset GetUtcNow(){if(trace.AwaitFinalClock&&trace.Active is {} attempt&&!attempt.Transaction.IsSettled){var tx=services.GetRequiredService<IDomainTransactionAccessor>().Current!;Assert.Same(attempt.Transaction,tx);trace.AwaitFinalClock=false;attempt.Steps.Add("clock");attempt.LoadTransactions.Add(tx);}return actual.GetUtcNow();}
    }
    public class QueryProxy:DispatchProxy
    {
        private ISelectedSessionDomainService actual=null!;private IServiceProvider services=null!;private Trace trace=null!;
        internal static ISelectedSessionDomainService Create(ISelectedSessionDomainService actual,IServiceProvider services,Trace trace){var value=Create<ISelectedSessionDomainService,QueryProxy>();var proxy=(QueryProxy)value;proxy.actual=actual;proxy.services=services;proxy.trace=trace;return value;}
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(method!.Name==nameof(ISelectedSessionDomainService.FindByConnection)&&trace.Stale is {} stale)return stale;
            if(method.Name==nameof(ISelectedSessionDomainService.IsInWorld)&&trace.Stale is not null){trace.GuardTransaction=services.GetRequiredService<IDomainTransactionAccessor>().Current as DomainTransaction;trace.GuardChain=GameplayJob.Current;}
            if(method.Name==nameof(ISelectedSessionDomainService.IsInWorld)&&trace.ComposerFault&&trace.Active?.Transaction.Completion==DomainTransactionCompletion.Committed)throw trace.Cause;
            try{return method.Invoke(actual,args);}catch(TargetInvocationException error)when(error.InnerException is {} cause){ExceptionDispatchInfo.Throw(cause);throw;}
        }
    }
    public class OnlineProxy:DispatchProxy
    {
        private IOnlineUserQueryDomainService actual=null!;private Trace trace=null!;
        internal static IOnlineUserQueryDomainService Create(IOnlineUserQueryDomainService actual,Trace trace){var value=Create<IOnlineUserQueryDomainService,OnlineProxy>();var proxy=(OnlineProxy)value;proxy.actual=actual;proxy.trace=trace;return value;}
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(method!.Name==nameof(IOnlineUserQueryDomainService.TryGetByConnection))
            {
                trace.OnlineReads++;
                if(trace.OnlineMode=="noOnline"||trace.OnlineMode=="changedOnline"&&trace.OnlineReads>1){args![1]=null;return false;}
            }
            try{return method.Invoke(actual,args);}catch(TargetInvocationException error)when(error.InnerException is {} cause){ExceptionDispatchInfo.Throw(cause);throw;}
        }
    }
    private sealed class ObserveUnits(ICommandWorkUnits actual,Trace trace):ICommandWorkUnits
    {
        private void Record(CommandExecutionContext context,CommandWorkUnitDeclaration declaration)
        {
            trace.Contexts.Add(context);trace.Declarations.Add(declaration);
            var plain=Assert.IsType<CommandRequestContext.ProtocolNonEffect>(context.Request);
            var metadata=plain.Message switch { SortItemRequest item=>item.Metadata,SortLockRequest locking=>locking.Metadata,_=>throw new InvalidOperationException("Unexpected NonEffect sort CLR.") };
            Assert.Same(metadata,Assert.IsType<ProtocolFrameMetadata.Game>(plain.Metadata).Value);
            Assert.Empty(declaration.Participants);Assert.Equal(CommandWorkUnitKind.Ordinary,declaration.Kind);Assert.Equal(CommandFootprintMode.None,declaration.Footprint);
        }
        public ValueTask<TResult> WriteAsync<TResult>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CommandWorkUnit,CancellationToken,ValueTask<TResult>> action,CancellationToken ct=default)where TResult:notnull
        {Record(context,declaration);return actual.WriteAsync(context,declaration,action,ct);}
        public ValueTask<CommandWriteResult<TResult>> WriteResultAsync<TResult>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CommandWorkUnit,CancellationToken,ValueTask<TResult>> action,CancellationToken ct=default)where TResult:notnull=>throw new InvalidOperationException("Sort must not acquire commit-result recovery.");
        public ValueTask<TResult> ReadOnlyAsync<TResult>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CancellationToken,ValueTask<TResult>> action,CancellationToken ct=default)where TResult:notnull=>throw new InvalidOperationException("Sort must retain its original write transaction.");
    }
    internal sealed record LogRow(LogLevel Level,Exception? Error,string Message,IReadOnlyDictionary<string,object?> Fields);
    private sealed class Logs(Trace trace):ILoggerProvider
    {
        public ILogger CreateLogger(string name)=>new Logger(trace);public void Dispose(){}
        private sealed class Logger(Trace trace):ILogger
        {public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;public bool IsEnabled(LogLevel level)=>true;public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> format)=>trace.Logs.Add(new(level,error,format(state,error),state is IEnumerable<KeyValuePair<string,object?>> fields ? fields.ToDictionary(x=>x.Key,x=>x.Value) : new Dictionary<string,object?>()));}
    }
    private sealed class Keys:IInventorySortKeySource
    {public ValueTask<InventorySortKeys> GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult(new InventorySortKeys(0,id==100310669?80:1,0,""));}
    private sealed class Placements:IItemPlacementDefinitionSource
    {public ValueTask<ItemPlacement?> GetAsync(int id,CancellationToken ct)=>id==3000?ValueTask.FromResult<ItemPlacement?>(new(1,1,new(BusinessInventorySpace.CarriedEquipment,0,47)){WorldClass=ItemWorldClass.Equipment}):new NpcShopTestContent().GetAsync(id,ct);}
    private static async ValueTask Stage(IServiceProvider services,Trace trace,Attempt attempt,CancellationToken ct)
    {attempt.ForeignKeys=await Scalar(services,"PRAGMA foreign_keys",ct);attempt.Children=trace.Native?await Scalar(services,"SELECT count(*) FROM sort_child",ct):0;attempt.Stage=await AssetsIn(services,ct);}
    internal sealed record AssetState(string Items,string Slots,string Stats,string Parameters,string Cargo,string Events,string Deliveries);
    private static Task<AssetState> Assets(ShopFixture f)=>TestGameplay.RunAsync(f.Services,async()=>await AssetsIn(f.Services,default));
    private static async ValueTask<AssetState> AssetsIn(IServiceProvider s,CancellationToken ct)=>new(
        await Rows(s,"SELECT * FROM items ORDER BY item_uid",ct),await Rows(s,"SELECT * FROM item_slots ORDER BY owner_scope,owner_id,list_type,slot_index",ct),
        await Rows(s,"SELECT * FROM character_stats ORDER BY character_id",ct),await Rows(s,"SELECT * FROM character_container_state ORDER BY character_id,list_type",ct),
        await Rows(s,"SELECT * FROM account_cargo_state ORDER BY account_id",ct),await Rows(s,"SELECT * FROM domain_events ORDER BY event_id",ct),await Rows(s,"SELECT * FROM domain_event_deliveries",ct));
    private static ValueTask<string> Rows(IServiceProvider s,string sql,CancellationToken ct)=>s.GetRequiredService<SqliteDatabase>().ReadAsync(async(cmd,token)=>{cmd.CommandText=sql;await using var reader=await cmd.ExecuteReaderAsync(token);var rows=new List<object>();while(await reader.ReadAsync(token))rows.Add(Enumerable.Range(0,reader.FieldCount).ToDictionary(i=>reader.GetName(i),i=>reader.IsDBNull(i)?null:reader.GetValue(i)is byte[] blob?Convert.ToHexString(blob):Convert.ToString(reader.GetValue(i))));return JsonSerializer.Serialize(rows);},ct);
    private static ValueTask<int> Scalar(IServiceProvider s,string sql,CancellationToken ct)=>s.GetRequiredService<SqliteDatabase>().ReadAsync(async(cmd,token)=>{cmd.CommandText=sql;return Convert.ToInt32(await cmd.ExecuteScalarAsync(token));},ct);
}

internal sealed record WireObservation(byte Selector,ushort Type,ushort Sequence,long? InboundOrdinal,bool SequenceIsFresh,int? FullBodyLength,string FullBodySha256,string Body,string CLR,int Logical);
internal static class InventorySortWire
{
    private sealed class State{internal ushort Sequence;internal readonly GameInboundFrameCounter Counter=new();}
    private static readonly ConditionalWeakTable<ShopFixture,State> states=new();
    internal static async ValueTask<IClientNetworkMessage> Send(ShopFixture f,ushort type,byte[] body,ushort? sequence=null,List<WireObservation>? observations=null)
    {
        var state=states.GetOrCreateValue(f);var seq=sequence??++state.Sequence;var(ordinal,fresh)=state.Counter.Observe(seq);
        var metadata=new GameMessageMetadata(1,type,0,seq){InboundOrdinal=ordinal,SequenceIsFresh=fresh};metadata=metadata.WithRawBody(body);
        var registration=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==metadata.Selector&&x.Type==type);
        var decoded=((IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType)).Decode(metadata,body);
        var actual=decoded switch{SortItemRequest sort=>sort.Metadata,SortLockRequest locking=>locking.Metadata,_=>throw new InvalidOperationException("Unexpected sort CLR.")};
        Assert.Same(metadata,actual);Assert.Equal(body.Length,actual.FullBodyLength);Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)),actual.FullBodySha256);
        observations?.Add(new(actual.Selector,actual.Type,actual.Sequence,actual.InboundOrdinal,actual.SequenceIsFresh,actual.FullBodyLength,actual.FullBodySha256!,Convert.ToHexString(body),decoded.GetType().FullName!,(int)decoded.MessageType));
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),decoded,default);return decoded;
    }
    internal static ValueTask<IClientNetworkMessage> Sort(ShopFixture f,BusinessInventorySpace space,InventorySortOrder order=InventorySortOrder.Default)=>Send(f,0x0014,space switch{
        BusinessInventorySpace.CarriedEquipment=>[0,1,(byte)order],BusinessInventorySpace.Consumable=>[0,2,(byte)order],BusinessInventorySpace.Material=>[0,3,(byte)order],
        BusinessInventorySpace.PersonalCargo=>[2,11,(byte)order],BusinessInventorySpace.AccountCargo=>[12,0,(byte)order],_=>throw new ArgumentOutOfRangeException(nameof(space))});
    internal static ValueTask<IClientNetworkMessage> Lock(ShopFixture f,GameSlot slot,InventorySortLockAction action){byte[] body=[(byte)slot.Space,0,0];BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1),checked((short)slot.Index));return Send(f,action==InventorySortLockAction.Toggle?(ushort)0x02CA:(ushort)0x02CB,body);}
}
