using Dfo.Server.Application.Tests.Support;
using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class RentalProtocolCommandFlowTests(ITestOutputHelper output)
{
    [Dfo.Server.Testing.PvfTheory] [InlineData(false)] [InlineData(true)]
    public async Task Actual_decoder_sqlite_encoder_and_sender_keep_assets_and_frame_order(bool charge)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var owner = Create(delivery); var f = owner.Host; var physical = delivery.Add(f.Connection);
        await Send(f, charge); await delivery.Settled();
        Assert.Equal(charge ? new ushort[] { 0x3D0, 0x0D, 0x3C1 } : new ushort[] { 0x0D, 0x3CF, 0x3C1 }, Split(physical.Sends).Select(Type));
        Assert.Equal(charge ? 140 : 97, await Stars(f)); Assert.Equal(charge ? 1000000 : 5000000, f.Gold);
        Assert.Equal(charge ? 0 : 1, f.Count(401000037)); Assert.Equal(0, physical.CloseCalls);
        Assert.Equal(EffectOperationState.Committed, Entry(f).State);
        output.WriteLine($"charge={charge} order={string.Join(',', Split(physical.Sends).Select(Type))} stars={await Stars(f)} gold={f.Gold}");
    }
    [Dfo.Server.Testing.PvfTheory] [InlineData(false)] [InlineData(true)]
    public async Task Committed_replay_actual_encoder_failure_preserves_entry_and_closes_physical_source(bool charge)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var owner = Create(delivery); var f = owner.Host; var physical = delivery.Add(f.Connection);
        await Send(f, charge); await delivery.Settled(); var entry = Entry(f); var ordinal = entry.Ordinal;
        var stars = await Stars(f); var gold = f.Gold; var count = f.Count(401000037); var sends = physical.Sends.Count;
        var cause = new IOException("TASK37 actual replay encoder"); var calls = 0;
        f.Sender.Outbound.Encoder.Fail((target, message) => { Assert.Same(f.Connection, target); Assert.Contains("InventorySnapshot", message.GetType().Name); calls++; return cause; });
        Assert.Same(cause, await Record.ExceptionAsync(() => Send(f, charge).AsTask()));
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(1, calls); Assert.Equal(1, physical.CloseCalls); Assert.Equal(sends, physical.Sends.Count);
        Assert.Equal(stars, await Stars(f)); Assert.Equal(gold, f.Gold); Assert.Equal(count, f.Count(401000037));
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Equal("close", physical.Events.Last().Kind);
        output.WriteLine($"charge={charge} actualEncoderCalls=1 physicalClose=1 unchangedAssets=True originalEntryOrdinal={ordinal}");
    }
    [Dfo.Server.Testing.PvfTheory] [InlineData(false)] [InlineData(true)]
    public async Task Refusal_is_empty_commit_without_refresh_or_close(bool charge)
    {
        using var owner = Create(); var f = owner.Host; var stars = await Stars(f); var gold = f.Gold;
        await Send(f, charge, invalid:true);
        Assert.Equal(stars, await Stars(f)); Assert.Equal(gold, f.Gold); Assert.Single(f.Sender.Outbound.Frames);
        Assert.Equal(new byte[] { 0, 1 }, f.Sender.Outbound.Frames.Single()[15..]);
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Empty(f.Sender.Outbound.Closes);
    }
    [Dfo.Server.Testing.PvfTheory] [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public async Task Real_action_and_native_commit_failures_roll_back_without_ack_and_close(bool charge, bool native)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var transactions = new SqliteTransactionProbe();
        using var owner = Create(delivery, transactions.Configure); var f = owner.Host; var physical = delivery.Add(f.Connection);
        var stars = await Stars(f); var gold = f.Gold;
        f.Sql(native ? "CREATE TABLE task37_parent(id INTEGER PRIMARY KEY); CREATE TABLE task37_child(id INTEGER REFERENCES task37_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER task37_fail AFTER UPDATE OF lucky_star ON accounts BEGIN INSERT INTO task37_child VALUES(404); END"
            : "CREATE TRIGGER task37_fail BEFORE UPDATE OF lucky_star ON accounts BEGIN SELECT RAISE(ABORT,'TASK37 action'); END");
        var before=LuckyStarRentalWireTests.DatabaseRows(f);
        var cause = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => Send(f, charge).AsTask());
        Assert.Equal(native ? 787 : 1811, cause.SqliteExtendedErrorCode);
        if(native)SqliteFailureAssertions.FromCommit(Assert.Single(transactions.Transactions), cause);
        Assert.Equal(before,LuckyStarRentalWireTests.DatabaseRows(f));
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(stars, await Stars(f)); Assert.Equal(gold, f.Gold); Assert.Equal(0, f.Count(401000037));
        Assert.Empty(physical.Sends); Assert.Empty(f.Sender.Messages); Assert.Equal(1, physical.CloseCalls);
        Assert.Equal(EffectOperationState.ConfirmedRollback, Entry(f).State);
        output.WriteLine($"charge={charge} native={native} sqliteExtended={cause.SqliteExtendedErrorCode} noACK=True physicalClose=1 rollback=True");
    }
    [Dfo.Server.Testing.PvfTheory] [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)] [InlineData(true,true)]
    public async Task Scope_disposal_keeps_committed_result_and_registered_frames_then_closes(bool charge,bool refusal)
    {
        var old=Environment.GetEnvironmentVariable("TASK37_EXPECT_ROUTE")=="legacy";
        var trace=new RentalTrace(); var cause=new IOException("TASK37 actual async scope disposal");
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var owner=Create(delivery,services=>{
            RentalObserver.Add<IEquipmentRentalDomainService>(services,trace); RentalObserver.Add<IAccountLuckyStarDomainService>(services,trace);
            var name=old?(charge?"ChargeRentPointCommand":"RentEquipmentCommand"):(charge?"GameChargeRentPointCommand":"GameRentEquipmentCommand");
            var descriptor=services.Last(x=>x.ServiceType.IsGenericType&&x.ServiceType.GetGenericTypeDefinition()==typeof(ICommandHandler<,>)&&x.ServiceType.GenericTypeArguments[0].Name==name);
            var wrapper=typeof(ScopeFault<,>).MakeGenericType(descriptor.ServiceType.GenericTypeArguments);
            services.AddScoped(descriptor.ServiceType,provider=>Activator.CreateInstance(wrapper,ActivatorUtilities.CreateInstance(provider,descriptor.ImplementationType!),cause)!);
        });
        var f=owner.Host; var physical=delivery.Add(f.Connection);
        var failure=await Record.ExceptionAsync(()=>Send(f,charge,invalid:refusal).AsTask());
        if(old)Assert.Same(cause,failure);else Assert.Same(cause,Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);
        Assert.Equal(DomainTransactionCompletion.Committed,trace.Main!.Completion);
        Assert.Equal(refusal?100:charge?140:97,await Stars(f)); Assert.Equal(refusal||!charge?5000000:1000000,f.Gold);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();
        Assert.Equal(1,physical.CloseCalls);Assert.Equal("close",physical.Events.Last().Kind);
        Assert.Equal(old?0:refusal?1:3,Split(physical.Sends).Count());
        Assert.Equal(!old,f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal(EffectOperationState.Committed,Entry(f).State);
        output.WriteLine($"old={old} charge={charge} refusal={refusal} committed=True frames={Split(physical.Sends).Count()} physicalClose=1");
    }
    public sealed class ScopeFault<TCommand,TResult>(object actual,Exception cause):ICommandHandler<TCommand,TResult>,IAsyncDisposable
        where TCommand:class,ICommand<TResult> where TResult:notnull
    {
        public ValueTask<TResult> HandleAsync(CommandExecutionContext context,TCommand command,CancellationToken ct)=>((ICommandHandler<TCommand,TResult>)actual).HandleAsync(context,command,ct);
        public ValueTask DisposeAsync()=>ValueTask.FromException(cause);
    }
    [Dfo.Server.Testing.PvfTheory] [InlineData(false,0)] [InlineData(true,0)] [InlineData(false,1)] [InlineData(true,1)] [InlineData(false,2)] [InlineData(true,2)]
    public async Task Independent_refresh_failure_keeps_Main_durable_and_closes_exact_source(bool charge,int kind)
    {
        var old=Environment.GetEnvironmentVariable("TASK37_EXPECT_ROUTE")=="legacy";
        var cause=kind==1?(Exception)new OperationCanceledException("TASK37 refresh cancellation"):new IOException("TASK37 refresh action");
        var trace=new RentalTrace {RefreshFailure=cause,FailRollback=kind==2};
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var owner=Create(delivery,services=>{ RentalObserver.Add<IEquipmentRentalDomainService>(services,trace);RentalObserver.Add<IAccountLuckyStarDomainService>(services,trace); });
        var f=owner.Host;var physical=delivery.Add(f.Connection);var replacement=f.Connection with {};var next=delivery.Add(replacement);
        var failure=await Record.ExceptionAsync(()=>Send(f,charge).AsTask());Assert.NotNull(failure);
        Assert.True(Contains(failure,cause));if(kind==2)Assert.True(Contains(failure,trace.RollbackCause));
        if(!old)Assert.IsType<TransactionOutcomeUncertainException>(failure);
        Assert.NotSame(trace.Main,trace.Refresh);Assert.Equal(DomainTransactionCompletion.Committed,trace.Main!.Completion);
        Assert.Equal(kind==2?DomainTransactionCompletion.Uncertain:DomainTransactionCompletion.ConfirmedRollback,trace.Refresh!.Completion);
        Assert.Equal(charge?140:97,await Stars(f));Assert.Equal(charge?1000000:5000000,f.Gold);
        Assert.Equal(EffectOperationState.Committed,Entry(f).State);Assert.Empty(f.Sender.Messages);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();
        Assert.Empty(physical.Sends);Assert.Equal(1,physical.CloseCalls);Assert.Equal(0,next.CloseCalls);Assert.Empty(next.Events);
        Assert.Equal(!old||kind==2,f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
        output.WriteLine($"old={old} charge={charge} kind={kind} main={trace.Main.Completion} refresh={trace.Refresh.Completion} originalCause=True independentCause={kind==2} physicalClose=1 replacementClose=0");
    }
    private static bool Contains(Exception? error,Exception cause)=>error is not null&&(ReferenceEquals(error,cause)||Contains(error.InnerException,cause)||error is AggregateException aggregate&&aggregate.InnerExceptions.Any(x=>Contains(x,cause)));
    internal sealed class RentalTrace
    {
        internal DomainTransaction? Main,Refresh;internal Exception? RefreshFailure;internal bool FailRollback;
        internal readonly Exception RollbackCause=new IOException("TASK37 independent rollback");
    }
    public class RentalObserver:DispatchProxy
    {
        private object actual=null!;private IServiceProvider services=null!;private RentalTrace trace=null!;
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>typeof(RentalObserver).GetMethod(nameof(Call),BindingFlags.Instance|BindingFlags.NonPublic)!.MakeGenericMethod(method!.ReturnType.GenericTypeArguments[0]).Invoke(this,[method,args]);
        private async ValueTask<T> Call<T>(MethodInfo method,object?[] args)
        {
            var transaction=(DomainTransaction?)services.GetRequiredService<IDomainTransactionAccessor>().Current;
            if(method.Name is "RentAsync" or "PurchaseAsync")trace.Main=transaction;
            if(method.Name=="PanelAsync"&&transaction is not null)
            {
                trace.Refresh=transaction;
                if(trace.FailRollback)transaction.Enlist(trace,()=>new RollbackFault(trace.RollbackCause));
                if(trace.RefreshFailure is {} cause)throw cause;
            }
            try{return await (ValueTask<T>)method.Invoke(actual,args)!;}
            catch(TargetInvocationException error)when(error.InnerException is {} cause){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(cause);throw;}
        }
        internal static void Add<T>(IServiceCollection services,RentalTrace trace)where T:class
        {
            var descriptor=services.Last(x=>x.ServiceType==typeof(T));services.AddSingleton<T>(provider=>{
                var proxy=Create<T,RentalObserver>();var observer=(RentalObserver)(object)proxy;
                observer.actual=descriptor.ImplementationInstance??descriptor.ImplementationFactory?.Invoke(provider)??ActivatorUtilities.CreateInstance(provider,descriptor.ImplementationType!);
                observer.services=provider;observer.trace=trace;return proxy;
            });
        }
    }
    private sealed class RollbackFault(Exception cause):ITransactionParticipant
    {
        public bool CommitCanFail=>false;
        public ValueTask PrepareAsync(CancellationToken ct)=>ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct)=>ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct)=>ValueTask.FromException(cause);
    }
    [Dfo.Server.Testing.PvfFact]
    public async Task Fresh_renewal_retains_uid_and_sets_absolute_deadline_in_distinct_write_units()
    {
        var trace=new RentalTrace();
        using var owner=Create(configure:services=>RentalObserver.Add<IEquipmentRentalDomainService>(services,trace));var f=owner.Host;
        await Send(f,false);
        var rental=f.Services.GetRequiredService<IEquipmentRentalDomainService>();var first=Assert.Single((await rental.PanelAsync(new(1),new(1),default)).Items);
        Assert.NotSame(trace.Main,trace.Refresh);Assert.False(trace.Refresh!.IsReadOnly);
        Assert.Equal(DomainTransactionCompletion.Committed,trace.Main!.Completion);Assert.Equal(DomainTransactionCompletion.Committed,trace.Refresh.Completion);
        f.Clock.Advance(TimeSpan.FromHours(2));await Send(f,false,sequence:8);
        var renewed=Assert.Single((await rental.PanelAsync(new(1),new(1),default)).Items);
        Assert.Equal(first.Uid,renewed.Uid);Assert.Equal(first.ExpireTime+7200,renewed.ExpireTime);Assert.Equal(94,await Stars(f));Assert.Equal(1,f.Count(401000037));
    }
    [Dfo.Server.Testing.PvfFact]
    public async Task NoRoom_followed_by_scope_failure_is_not_misreported_as_uncertain_commit()
    {
        var old=Environment.GetEnvironmentVariable("TASK37_EXPECT_ROUTE")=="legacy";
        var trace=new RentalTrace();var cause=new IOException("TASK37 after ConfirmedRollback disposal");
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var owner=Create(delivery,services=>{
            RentalObserver.Add<IEquipmentRentalDomainService>(services,trace);
            var name=old?"RentEquipmentCommand":"GameRentEquipmentCommand";
            var descriptor=services.Last(x=>x.ServiceType.IsGenericType&&x.ServiceType.GetGenericTypeDefinition()==typeof(ICommandHandler<,>)&&x.ServiceType.GenericTypeArguments[0].Name==name);
            var wrapper=typeof(ScopeFault<,>).MakeGenericType(descriptor.ServiceType.GenericTypeArguments);
            services.AddScoped(descriptor.ServiceType,provider=>Activator.CreateInstance(wrapper,ActivatorUtilities.CreateInstance(provider,descriptor.ImplementationType!),cause)!);
        });
        var f=owner.Host;var physical=delivery.Add(f.Connection);
        for(var slot=9;slot<=40;slot++)await f.SeedCore(slot,Dfo.Server.Data.Inventory.StackableItemCore.Create(1,401000037,123));
        var before=LuckyStarRentalWireTests.DatabaseRows(f);
        Assert.Same(cause,await Record.ExceptionAsync(()=>Send(f,false).AsTask()));
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,trace.Main!.Completion);Assert.Equal(before,LuckyStarRentalWireTests.DatabaseRows(f));
        Assert.Equal(EffectOperationState.ConfirmedRollback,Entry(f).State);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();
        Assert.Equal(old?0:1,Split(physical.Sends).Count());Assert.Equal("close",physical.Events.Last().Kind);
        if(!old)Assert.Equal(new byte[]{1,2,0,0,0},Assert.Single(Split(physical.Sends))[15..]);
        output.WriteLine($"old={old} rollback=ConfirmedRollback noUncertain=True frames={Split(physical.Sends).Count()} closeLast=True");
    }
    [Dfo.Server.Testing.PvfTheory] [InlineData(false)] [InlineData(true)]
    public async Task Initial_required_encoder_failure_retains_Main_and_uses_default_whole_protection(bool charge)
    {
        var old=Environment.GetEnvironmentVariable("TASK37_EXPECT_ROUTE")=="legacy";
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();using var owner=Create(delivery);
        var f=owner.Host;var physical=delivery.Add(f.Connection);var cause=new IOException("TASK37 initial required encoder");
        f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);return cause;});
        var failure=await Record.ExceptionAsync(()=>Send(f,charge).AsTask());
        if(old)Assert.Same(cause,failure);else Assert.Same(cause,Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);
        Assert.Equal(charge?140:97,await Stars(f));Assert.Equal(charge?1000000:5000000,f.Gold);Assert.Equal(EffectOperationState.Committed,Entry(f).State);
        Assert.Equal(!old,f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();Assert.Empty(physical.Sends);Assert.Equal(1,physical.CloseCalls);
        output.WriteLine($"old={old} charge={charge} initialRequired=True blocked={!old} Main=Committed physicalClose=1");
    }
    [Dfo.Server.Testing.PvfTheory] [InlineData(false)] [InlineData(true)]
    public async Task Actual_replay_encoder_close_does_not_close_same_id_replacement_or_unrelated_transport(bool charge)
    {
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();using var owner=Create(delivery);var f=owner.Host;
        var original=delivery.Add(f.Connection);await Send(f,charge);await delivery.Settled();var entry=Entry(f);var ordinal=entry.Ordinal;
        var replacement=f.Connection with {};var next=delivery.Add(replacement);
        var unrelated=f.Connection with {ConnectionId=new("task37-unrelated")};var other=delivery.Add(unrelated);var cause=new IOException("TASK37 replay exact close");
        f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);f.Sender.Outbound.Connections.Remove(target.ConnectionId);f.Sender.Outbound.Connections.Add(replacement);return cause;});
        Assert.Same(cause,await Record.ExceptionAsync(()=>Send(f,charge).AsTask()));
        await original.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();
        Assert.Equal(1,original.CloseCalls);Assert.Equal(0,next.CloseCalls);Assert.Equal(0,other.CloseCalls);Assert.Empty(next.Events);Assert.Empty(other.Events);
        Assert.Same(entry,Entry(f));Assert.Equal(ordinal,entry.Ordinal);Assert.Equal(EffectOperationState.Committed,entry.State);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(unrelated));
    }
    internal static CeraShopFixture Create(RemainingOpeningProtocolCommandFlowTests.SenderProbe? delivery=null, Action<IServiceCollection>? configure=null)
    {
        var owner = new CeraShopFixture(useArchive:true, configure: services => {
            services.AddSingleton<ICharacterSkillPointDomainService,Dfo.Server.Application.Characters.CharacterSkillPointDomainService>();
            if(delivery is not null) services.AddSingleton<IOutboundSender>(delivery.Recorder);
            configure?.Invoke(services);
        });
        owner.Host.Sql("UPDATE accounts SET lucky_star=100; UPDATE characters SET job=0; UPDATE character_stats SET carried_gold=5000000; INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0); INSERT INTO character_pvp(character_id) VALUES(1); INSERT INTO character_settings(character_id) VALUES(1); INSERT INTO character_expert_job(character_id) VALUES(1);");
        return owner;
    }
    internal static ValueTask Send(ShopFixture f, bool charge, ushort sequence=7, bool invalid=false)
    {
        var body = new byte[22]; BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(14),charge?1:invalid?0:401000037);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(18),charge?(invalid?0:40):0);
        return EffectRequestTransport.SendRaw(f,new(1,charge?(ushort)0x3D0:(ushort)0x3CF,0,sequence),body);
    }
    private static ValueTask<int> Stars(ShopFixture f) => f.Services.GetRequiredService<IAccountLuckyStarDomainService>().BalanceAsync(new(1),default);
    internal static EffectOperationEntry Entry(ShopFixture f)
    {
        var ledger=f.Services.GetRequiredService<EffectOperationLedger>(); var table=typeof(EffectOperationLedger).GetField("ledgers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ledger)!;
        object?[] args=[f.Connection,null]; Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,args)!);
        return Assert.Single(((IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!).Values.Cast<EffectOperationEntry>());
    }
    private static IEnumerable<byte[]> Split(IEnumerable<byte[]> batches)
    {
        foreach(var bytes in batches) for(var offset=0;offset<bytes.Length;) { var length=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset+3))); yield return bytes[offset..(offset+length)]; offset+=length; }
    }
    private static ushort Type(byte[] frame)=>BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
}
