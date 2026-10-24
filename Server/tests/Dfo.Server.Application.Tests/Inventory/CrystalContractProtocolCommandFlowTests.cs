using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.CrystalContract;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class CrystalContractProtocolCommandFlowTests(ITestOutputHelper output)
{
    // Run on the original registered decoder/handler before changing the production boundary.
    [Theory]
    [InlineData(0,0)] [InlineData(0,5)] [InlineData(1,3)] [InlineData(0,255)]
    public async Task Real_two_byte_wire_commits_only_current_character_selection_before_one_batch(byte mode,byte selection)
    {
        var t=new Trace();using var f=await Fixture(t);var before=await Assets(f);
        f.Sender.BeforeSend=()=>{Assert.Equal(mode,f.Scalar("SELECT crystal_cube_type FROM character_condition WHERE character_id=1"));Assert.Equal(selection,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1"));};
        await CrystalWire.Send(f,mode,selection,trace:t);var after=await Assets(f);
        Assert.Equal(mode,f.Scalar("SELECT crystal_cube_type FROM character_condition WHERE character_id=1"));Assert.Equal(selection,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1"));
        Assert.Equal(28,f.Scalar("SELECT stamina FROM character_condition WHERE character_id=1"));Assert.Equal(4,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=2"));
        Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(t.Attempts).Tx.Completion);AssertNullTransaction(t);var batch=Assert.Single(f.Sender.Batches);Assert.Same(f.Connection,batch.Connection);Assert.Equal(2,batch.Frames.Length);
        var ack=Assert.IsType<CrystalContractResponse>(f.Sender.Messages[0]);Assert.Equal((true,mode,selection),(ack.Success,ack.Mode,ack.Selection));Assert.Equal("CubeProgressionStateMessage",f.Sender.Messages[1].GetType().Name);AssertNoLedger(f);
        Write("success",new{mode,selection,before,after,attempts=Attempts(t),inputs=t.Inputs,frames=Frames(f)});
    }
    [Theory] [InlineData(2,0)] [InlineData(0,6)] [InlineData(0,254)]
    public async Task Invalid_mode_or_selection_commits_normal_refusal_without_changing_assets(byte mode,byte selection)
    {var t=new Trace();using var f=await Fixture(t);var before=await Assets(f);await CrystalWire.Send(f,mode,selection,trace:t);Assert.Equal(before,await Assets(f));Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(t.Attempts).Tx.Completion);var ack=Assert.IsType<CrystalContractResponse>(Assert.Single(f.Sender.Messages));Assert.Equal((false,(byte)0,(byte)255),(ack.Success,ack.Mode,ack.Selection));Write("invalid",new{mode,selection,before,after=await Assets(f),attempts=Attempts(t),inputs=t.Inputs,frames=Frames(f)});}
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Missing_or_exactly_expired_entitlement_preserves_complete_selection(bool expired)
    {var t=new Trace();using var f=await Fixture(t,expired?"expired":"missing");var before=await Assets(f);await CrystalWire.Send(f,0,1,trace:t);Assert.Equal(before,await Assets(f));Assert.False(Assert.IsType<CrystalContractResponse>(Assert.Single(f.Sender.Messages)).Success);Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(t.Attempts).Tx.Completion);Write("entitlement",new{expired,before,after=await Assets(f),attempts=Attempts(t),inputs=t.Inputs,frames=Frames(f)});}
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task SQL_action_and_actual_native_Commit_keep_cause_and_all_condition_columns_atomic(bool native)
    {
        var t=new Trace{Native=native};using var f=await Fixture(t);var before=await Assets(f);
        var ddl=native?"CREATE TABLE crystal_parent(id INTEGER PRIMARY KEY);CREATE TABLE crystal_child(id INTEGER REFERENCES crystal_parent(id) DEFERRABLE INITIALLY DEFERRED);CREATE TRIGGER crystal_fault AFTER UPDATE OF crystal_cube_type,crystal_cube_grade ON character_condition WHEN NEW.character_id=1 BEGIN INSERT INTO crystal_child VALUES(404);END;":"CREATE TRIGGER crystal_fault BEFORE UPDATE ON character_condition WHEN NEW.character_id=1 BEGIN SELECT RAISE(ABORT,'crystal action SQL failure');END;";
        f.Sql(ddl);var error=await Record.ExceptionAsync(()=>CrystalWire.Send(f,1,3,sequence:7,trace:t).AsTask());var cause=Assert.IsType<SqliteException>(error);Assert.Equal(19,cause.SqliteErrorCode);Assert.Equal(native?787:1811,cause.SqliteExtendedErrorCode);var a=Assert.Single(t.Attempts);Assert.Equal(native,a.Completed);Assert.Equal(1,a.ForeignKeys);Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,a.Tx.Completion);Assert.Equal(before,await Assets(f));
        if(native){Assert.True(a.Children>0);Assert.Equal((1,3),(a.Mode,a.Selection));Assert.Same(cause,a.Tx.CommitFailure!.Cause);Assert.Contains("SqliteTransaction.Commit",cause.StackTrace!,StringComparison.Ordinal);}else{Assert.Same(cause,a.Failure);Assert.DoesNotContain("SqliteTransaction.Commit",cause.StackTrace!,StringComparison.Ordinal);}
        Assert.Empty(f.Sender.Batches);var close=Assert.Single(f.Sender.Outbound.Entries);Assert.True(close.IsClose);Assert.Same(f.Connection,close.Connection);Assert.True(GameplayJob.EndedAJob(cause));Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));AssertNoLedger(f);
        var fault=new{native,ddl,before,after=await Assets(f),attempts=Attempts(t),cause=cause.ToString(),causeSame=ReferenceEquals(cause,native?a.Tx.CommitFailure!.Cause:a.Failure),sourceCloseExact=ReferenceEquals(f.Connection,close.Connection),inputs=t.Inputs.ToArray()};
        f.Sql("DROP TRIGGER crystal_fault");await CrystalWire.Send(f,1,3,sequence:7,trace:t);Assert.Equal(3,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1"));Assert.False(t.Inputs.Last().SequenceIsFresh);Assert.Single(f.Sender.Batches);Write("sql",new{fault,afterRetry=await Assets(f),inputs=t.Inputs,frames=Frames(f)});
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Actual_encoder_or_ComposeAsync_fault_stays_isolated_Failed_after_saved_choice(bool composer)
    {var t=new Trace{ComposerFault=composer};using var f=await Fixture(t);int calls=0;if(!composer)f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);calls++;return t.Cause;});var error=await Record.ExceptionAsync(()=>CrystalWire.Send(f,1,3,trace:t).AsTask());Assert.Null(error);Assert.Equal(3,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1"));Assert.Equal(NotificationOutcome.Failed,Assert.Single(t.Delivery!.Attempts).Outcome);Assert.Single(t.Logs,x=>x.Level==LogLevel.Warning&&ReferenceEquals(x.Error,t.Cause));Assert.Equal(composer?1:0,t.ComposerFaults);Assert.Empty(f.Sender.Batches);Assert.Empty(f.Sender.Outbound.Closes);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));AssertNoLedger(f);Write("projection",new{composer,calls,composerFaults=t.ComposerFaults,assets=await Assets(f),attempts=Attempts(t),inputs=t.Inputs,frames=Frames(f)});}
    [Theory] [InlineData("invalid")] [InlineData("missing")]
    public async Task Normal_committed_refusal_ACK_keeps_exact_cause_close_and_root_chain(string branch)
    {
        var t=new Trace();using var f=await Fixture(t,branch=="missing"?"missing":"active");var before=await Assets(f);int calls=0;f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);t.Chain=GameplayJob.Current;calls++;return t.Cause;});
        var error=await Record.ExceptionAsync(()=>CrystalWire.Send(f,branch=="invalid"?(byte)2:(byte)0,1,trace:t).AsTask());
        if(t.Contexts.Count>0){var typed=Assert.IsType<TransactionOutcomeUncertainException>(error);Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure,typed.Reason);Assert.Same(t.Cause,typed.InnerException);var request=Assert.IsType<CommandRequestContext.ProtocolNonEffect>(t.Contexts.Single().Request);Assert.True(request.IsClosed);Assert.True(request.HasCommittedUnit);Assert.Empty(request.CommittedParticipants);Assert.Empty((System.Collections.ICollection)typeof(CommandRequestContext).GetField("attempts",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(request)!);}else Assert.Same(t.Cause,error);
        Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(t.Attempts).Tx.Completion);Assert.Equal(before,await Assets(f));Assert.True(GameplayJob.EndedAJob(error!));Assert.Equal(1,calls);Assert.Empty(f.Sender.Messages);var close=Assert.Single(f.Sender.Outbound.Entries);Assert.True(close.IsClose);Assert.Same(f.Connection,close.Connection);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));AssertNoLedger(f);
        var chain=t.Chain!.Value;Assert.True(chain.IsInitialized);Assert.Equal(0,chain.Depth);Assert.Null(chain.ParentId);Assert.Equal(GameplayCallSource.Connection,chain.Source);Assert.Equal(f.Connection.ConnectionId,chain.SourceConnection);var log=Assert.Single(t.Logs,x=>x.Level==LogLevel.Error&&ReferenceEquals(x.Error,error));foreach(var pair in new Dictionary<string,object?>{{"RootId",chain.RootId},{"ParentId",chain.ParentId},{"Depth",chain.Depth},{"Source",chain.Source},{"SourceConnection",chain.SourceConnection},{"CommandType",chain.CommandType}})Assert.Equal(pair.Value,log.Fields[pair.Key]);
        Write("refusal",new{branch,before,after=await Assets(f),cause=error!.ToString(),causeSame=ReferenceEquals(error is TransactionOutcomeUncertainException x?x.InnerException:error,t.Cause),typed=TransactionOutcomeUncertainException.Contains(error),reason=(error as TransactionOutcomeUncertainException)?.Reason.ToString(),chain,attempts=Attempts(t),inputs=t.Inputs,sourceCloseExact=ReferenceEquals(f.Connection,close.Connection),endedAJob=GameplayJob.EndedAJob(error),frames=Frames(f)});
    }
    [Fact]
    public async Task Saved_choice_remains_committed_when_the_post_save_world_query_reports_not_current()
    {var t=new Trace{PostWorldFalse=true};using var f=await Fixture(t);await CrystalWire.Send(f,1,3,trace:t);Assert.Equal(3,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1"));Assert.Equal(1,t.PostWorldChecks);Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(t.Attempts).Tx.Completion);Assert.Empty(f.Sender.Messages);Assert.Empty(f.Sender.Outbound.Closes);Write("postWorld",new{assets=await Assets(f),attempts=Attempts(t),checks=t.PostWorldChecks,inputs=t.Inputs,frames=Frames(f),scope="read-only phase proxy reports false after actual committed save; branch-order proof, not physical concurrent exit"});}
    [Fact]
    public async Task Real_previous_session_query_preserves_domain_refusal_and_post_world_silence()
    {var t=new Trace();using var f=await Fixture(t);var old=f.Session;f.Select(2);t.Stale=old;var before=await Assets(f);await CrystalWire.Send(f,0,1,trace:t);Assert.Equal(before,await Assets(f));Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(t.Attempts).Tx.Completion);Assert.Empty(f.Sender.Messages);Assert.Empty(f.Sender.Outbound.Closes);Write("stale",new{before,after=await Assets(f),attempts=Attempts(t),inputs=t.Inputs,frames=Frames(f),scope="query returns actual old session; IsInWorld uses real registry; wire contains no old-session field"});}
    [Fact]
    public async Task Same_sequence_reapplies_current_requested_choice_and_accepts_new_NonEffect_payload()
    {var t=new Trace();using var f=await Fixture(t);await CrystalWire.Send(f,1,3,sequence:7,trace:t);f.Sql("UPDATE character_condition SET crystal_cube_type=0,crystal_cube_grade=5 WHERE character_id=1");await CrystalWire.Send(f,1,3,sequence:7,trace:t);Assert.Equal(3,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1"));await CrystalWire.Send(f,0,0,sequence:7,trace:t);Assert.Equal(0,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1"));Assert.Equal(new[]{true,false,false},t.Inputs.Select(x=>x.SequenceIsFresh));Assert.Equal(3,t.Attempts.Count);Assert.Equal(3,f.Sender.Batches.Count);AssertNoLedger(f);Write("sameSeq",new{assets=await Assets(f),attempts=Attempts(t),inputs=t.Inputs,frames=Frames(f)});}
    [Theory] [InlineData(1,3)] [InlineData(0,0)]
    public async Task Legal_selection_is_stored_exactly_in_full_condition_row(byte mode,byte selection)
    {var t=new Trace();using var f=await Fixture(t);var before=await Assets(f);await CrystalWire.Send(f,mode,selection,trace:t);var actual=f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1");Write("mutationAsset",new{mode,selection,actual,before,after=await Assets(f),attempts=Attempts(t),inputs=t.Inputs,frames=Frames(f)});Assert.Equal(selection,actual);Assert.Equal(mode,f.Scalar("SELECT crystal_cube_type FROM character_condition WHERE character_id=1"));}
    [Fact]
    public async Task Missing_selected_session_is_silent_and_opens_no_domain_unit()
    {var t=new Trace{MissingSession=true};using var f=await Fixture(t);var before=await Assets(f);await CrystalWire.Send(f,0,1,trace:t);Assert.Equal(before,await Assets(f));Assert.Empty(t.Attempts);Assert.Empty(f.Sender.Messages);Write("missingSession",new{before,after=await Assets(f),inputs=t.Inputs,frames=Frames(f),scope="read-only selected-session query returns null"});}

    private void Write(string kind,object data)=>output.WriteLine(JsonSerializer.Serialize(new{task25=true,kind,data}));
    private static void AssertNullTransaction(Trace t)=>Assert.Null(t.ProjectionTransaction);
    private static object[] Attempts(Trace t)=>t.Attempts.Select(a=>(object)new{completion=a.Tx.Completion.ToString(),a.Completed,failure=a.Failure?.ToString(),a.ForeignKeys,a.Children,a.Mode,a.Selection,a.Stage}).ToArray();
    private static string[] Frames(ShopFixture f)=>f.Sender.Outbound.Frames.Select(Convert.ToHexString).ToArray();
    private static async Task<ShopFixture> Fixture(Trace t,string entitlement="active")
    {
        var f=new ShopFixture(services=>{
            services.AddSingleton<CrystalContractDomainService>();services.AddSingleton<ICrystalContractDomainService>(p=>new ObserveCrystal(p.GetRequiredService<CrystalContractDomainService>(),p,t));
            var selected=services.Last(x=>x.ServiceType==typeof(ISelectedSessionDomainService));services.AddSingleton<ISelectedSessionDomainService>(p=>QueryProxy.Create(Resolve<ISelectedSessionDomainService>(selected,p),t));
            var ev=services.Last(x=>x.ServiceType==typeof(IEventService));services.AddSingleton<ObserveEvents>(p=>new(Resolve<IEventService>(ev,p),p,t));services.AddSingleton<IEventService>(p=>p.GetRequiredService<ObserveEvents>());services.AddSingleton<ICommandProjections>(p=>p.GetRequiredService<ObserveEvents>());
            var composer=services.Last(x=>x.ServiceType==typeof(INotificationHandler<CrystalContractSelected>));services.AddScoped<INotificationHandler<CrystalContractSelected>>(p=>new ObserveComposer(Resolve<INotificationHandler<CrystalContractSelected>>(composer,p),t));
            services.AddScoped<CommandWorkUnits>();services.AddScoped<ICommandWorkUnits>(p=>new ObserveUnits(p.GetRequiredService<CommandWorkUnits>(),t));services.AddLogging(log=>log.AddProvider(new Logs(t)));
        });
        f.Sql("INSERT INTO character_condition(character_id,stamina,pc_room_state,creature_buffer,crystal_cube_type,crystal_cube_grade) VALUES(1,28,7,X'0102030405060708',0,255),(2,82,3,X'0807060504030201',1,4)");
        if(entitlement!="missing")f.Sql($"INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,97,{f.Clock.GetUtcNow().AddDays(entitlement=="active"?1:0).ToUnixTimeSeconds()})");
        await f.Seed(65,5000,2);return f;
    }
    private static T Resolve<T>(ServiceDescriptor d,IServiceProvider p)where T:class=>(T)(d.ImplementationInstance??d.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,d.ImplementationType!));
    internal sealed class Trace
    {internal readonly List<CrystalWireObservation> Inputs=[];internal readonly List<Attempt> Attempts=[];internal readonly List<LogRow> Logs=[];internal readonly List<CommandExecutionContext> Contexts=[];internal bool Native,ComposerFault,Capturing,PostWorldFalse,MissingSession;internal int ComposerFaults,PostWorldChecks;internal SelectedSessionIdentity? Stale;internal GameplayCallChain? Chain;internal NotificationDelivery? Delivery;internal IDomainTransaction? ProjectionTransaction;internal readonly IOException Cause=new("fixed crystal actual encoder or composer cause");}
    internal sealed record Attempt(DomainTransaction Tx){internal bool Completed;internal Exception? Failure;internal int ForeignKeys,Children,Mode,Selection;internal string? Stage;}
    private sealed class ObserveCrystal(ICrystalContractDomainService actual,IServiceProvider p,Trace t):ICrystalContractDomainService
    {public async ValueTask<bool> SelectAsync(SelectedSessionIdentity session,byte mode,byte selection,CancellationToken ct){var tx=Assert.IsType<DomainTransaction>(p.GetRequiredService<IDomainTransactionAccessor>().Current);Assert.False(tx.IsReadOnly);Assert.False(tx.IsSettled);var a=new Attempt(tx);t.Attempts.Add(a);bool saved=false;try{saved=await actual.SelectAsync(session,mode,selection,ct);a.Completed=true;return saved;}catch(Exception e){a.Failure=e;throw;}finally{if(saved||a.Failure!=null){a.ForeignKeys=await Scalar(p,"PRAGMA foreign_keys",ct);a.Children=t.Native?await Scalar(p,"SELECT count(*) FROM crystal_child",ct):0;a.Mode=await Scalar(p,"SELECT crystal_cube_type FROM character_condition WHERE character_id=1",ct);a.Selection=await Scalar(p,"SELECT crystal_cube_grade FROM character_condition WHERE character_id=1",ct);a.Stage=await AssetsIn(p,ct);}}}}
    private sealed class ObserveComposer(INotificationHandler<CrystalContractSelected> actual,Trace t):INotificationHandler<CrystalContractSelected>
    {public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,CrystalContractSelected notification,CancellationToken ct){if(t.Capturing&&t.ComposerFault){t.ComposerFaults++;throw t.Cause;}return actual.ComposeAsync(recipient,notification,ct);}}
    private sealed class ObserveEvents(IEventService actual,IServiceProvider p,Trace t):IEventService,ICommandProjections
    {public ValueTask<Guid> EnqueueAsync<T>(T payload,CancellationToken ct=default)where T:class,IDomainEvent=>actual.EnqueueAsync(payload,ct);public ValueTask DirectAsync<T>(T payload,CancellationToken ct=default)where T:class,IDomainEvent=>actual.DirectAsync(payload,ct);public async ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience,T notification,CancellationToken ct=default)where T:class,INotification<T>{if(t.Capturing)t.ProjectionTransaction=p.GetRequiredService<IDomainTransactionAccessor>().Current;var result=await actual.ProjectAsync(audience,notification,ct);if(t.Capturing)t.Delivery=result;return result;}}
    public class QueryProxy:DispatchProxy
    {private ISelectedSessionDomainService actual=null!;private Trace trace=null!;internal static ISelectedSessionDomainService Create(ISelectedSessionDomainService actual,Trace trace){var x=Create<ISelectedSessionDomainService,QueryProxy>();((QueryProxy)x).actual=actual;((QueryProxy)x).trace=trace;return x;}protected override object? Invoke(MethodInfo? method,object?[]? args){if(trace.Capturing&&method!.Name==nameof(ISelectedSessionDomainService.FindByConnection)){if(trace.MissingSession)return null;if(trace.Stale is{} old)return old;}if(trace.Capturing&&trace.PostWorldFalse&&method!.Name==nameof(ISelectedSessionDomainService.IsInWorld)&&trace.Attempts.LastOrDefault()?.Tx.Completion==DomainTransactionCompletion.Committed){trace.PostWorldChecks++;return false;}try{return method!.Invoke(actual,args);}catch(TargetInvocationException e)when(e.InnerException is{} cause){ExceptionDispatchInfo.Throw(cause);throw;}}}
    private sealed class ObserveUnits(ICommandWorkUnits actual,Trace t):ICommandWorkUnits
    {public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull{var req=Assert.IsType<CommandRequestContext.ProtocolNonEffect>(c.Request);Assert.Empty(d.Participants);Assert.Equal(CommandWorkUnitKind.Ordinary,d.Kind);Assert.Equal(CommandFootprintMode.None,d.Footprint);Assert.Same(((CrystalContractRequest)req.Message).Metadata,Assert.IsType<ProtocolFrameMetadata.Game>(req.Metadata).Value);t.Contexts.Add(c);return actual.WriteAsync(c,d,a,ct);}public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new InvalidOperationException("Crystal uses ordinary Write.");public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new InvalidOperationException("Crystal retains its writable unit.");}
    internal sealed record LogRow(LogLevel Level,Exception? Error,string Message,IReadOnlyDictionary<string,object?> Fields);
    private sealed class Logs(Trace t):ILoggerProvider{public ILogger CreateLogger(string name)=>new Logger(t);public void Dispose(){}private sealed class Logger(Trace t):ILogger{public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;public bool IsEnabled(LogLevel l)=>true;public void Log<TState>(LogLevel l,EventId id,TState state,Exception? error,Func<TState,Exception?,string> format)=>t.Logs.Add(new(l,error,format(state,error),state is IEnumerable<KeyValuePair<string,object?>> fields?fields.ToDictionary(x=>x.Key,x=>x.Value):new Dictionary<string,object?>()));}}
    private static void AssertNoLedger(ShopFixture f){var table=typeof(Dfo.Server.Application.Effects.EffectOperationLedger).GetField("ledgers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Services.GetRequiredService<Dfo.Server.Application.Effects.EffectOperationLedger>())!;object?[] args=[f.Connection,null];Assert.False((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,args)!);}
    internal static Task<string> Assets(ShopFixture f)=>TestGameplay.RunAsync(f.Services,async()=>await AssetsIn(f.Services,default));
    private static async ValueTask<string> AssetsIn(IServiceProvider p,CancellationToken ct){var data=new SortedDictionary<string,string>();foreach(var table in new[]{"items","item_slots","character_condition","characters","character_stats","account_cargo_state","character_container_state","account_premiums","domain_events","domain_event_deliveries"})data[table]=await Rows(p,"SELECT * FROM "+table,ct);return JsonSerializer.Serialize(data);}
    private static ValueTask<string> Rows(IServiceProvider p,string sql,CancellationToken ct)=>p.GetRequiredService<SqliteDatabase>().ReadAsync(async(cmd,token)=>{cmd.CommandText=sql;await using var r=await cmd.ExecuteReaderAsync(token);var rows=new List<string>();while(await r.ReadAsync(token))rows.Add(JsonSerializer.Serialize(Enumerable.Range(0,r.FieldCount).ToDictionary(i=>r.GetName(i),i=>r.IsDBNull(i)?null:r.GetValue(i)is byte[] blob?Convert.ToHexString(blob):Convert.ToString(r.GetValue(i)))));rows.Sort(StringComparer.Ordinal);return "["+string.Join(",",rows)+"]";},ct);
    private static ValueTask<int> Scalar(IServiceProvider p,string sql,CancellationToken ct)=>p.GetRequiredService<SqliteDatabase>().ReadAsync(async(cmd,token)=>{cmd.CommandText=sql;return Convert.ToInt32(await cmd.ExecuteScalarAsync(token));},ct);
}
internal sealed record CrystalWireObservation(byte Selector,ushort Type,ushort Sequence,long? InboundOrdinal,bool SequenceIsFresh,int? FullBodyLength,string FullBodySha256,string Body,string CLR,int Logical,DateTimeOffset Started);
internal static class CrystalWire
{
    private sealed class State{internal ushort Sequence;internal readonly GameInboundFrameCounter Counter=new();}private static readonly ConditionalWeakTable<ShopFixture,State> states=new();
    internal static async ValueTask<IClientNetworkMessage> Send(ShopFixture f,byte mode,byte selection,ushort? sequence=null,CrystalContractProtocolCommandFlowTests.Trace? trace=null)
    {byte[] body=[mode,selection];var state=states.GetOrCreateValue(f);var seq=sequence??++state.Sequence;var(ordinal,fresh)=state.Counter.Observe(seq);var metadata=new GameMessageMetadata(1,0x0218,0,seq){InboundOrdinal=ordinal,SequenceIsFresh=fresh};metadata=metadata.WithRawBody(body);var reg=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1&&x.Type==0x0218);var decoded=Assert.IsType<CrystalContractRequest>(((IGameMessageDecoder)f.Services.GetRequiredService(reg.DecoderType)).Decode(metadata,body));Assert.Same(metadata,decoded.Metadata);Assert.Equal(2,metadata.FullBodyLength);Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)),metadata.FullBodySha256);trace?.Inputs.Add(new(metadata.Selector,metadata.Type,metadata.Sequence,metadata.InboundOrdinal,metadata.SequenceIsFresh,metadata.FullBodyLength,metadata.FullBodySha256!,Convert.ToHexString(body),decoded.GetType().FullName!,(int)decoded.MessageType,DateTimeOffset.UtcNow));if(trace!=null)trace.Capturing=true;try{await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),decoded,default);return decoded;}finally{if(trace!=null)trace.Capturing=false;}}
}
