using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Application.Transactions;
using System.Collections;
using Dfo.Server.Application.Effects;
using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Teleport;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class TeleportProtocolCommandFlowTests(ITestOutputHelper output)
{
    private static bool Legacy => typeof(TeleportHandler).Assembly.GetType("Dfo.Server.Protocol.Game.Teleport.GameTeleportCommand") is null;
    [Theory] [InlineData("initial")] [InlineData("scope")] [InlineData("replay")]
    public async Task Actual_encoder_and_scope_failures_keep_commit_identity_and_exact_physical_close(string mode)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace(); using var f = TeleportConsumableTests.Fixture(services =>
        {
            services.AddSingleton<IOutboundSender>(delivery.Recorder);
            services.AddSingleton(p => new GameplayOutboxes(p.GetRequiredService<IOutboundSender>(),p.GetRequiredService<GameplayCloseCauses>(),p.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GameplayOutbox>>()));
            Decorate<ITeleportConsumableDomainService>(services, trace);
            Decorate<IReplayStateDomainService>(services, trace);
            Decorate<ICreatureRuntimeDomainService>(services, trace);
            var descriptor = services.Last(x => x.ServiceType.IsGenericType && x.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) && x.ServiceType.GenericTypeArguments[0].Name == (Legacy ? "TeleportCommand" : "GameTeleportCommand"));
            if(mode == "scope") services.AddScoped(descriptor.ServiceType,p => Activator.CreateInstance(typeof(ScopeFault<,>).MakeGenericType(descriptor.ServiceType.GenericTypeArguments),ActivatorUtilities.CreateInstance(p,descriptor.ImplementationType!),trace)!);
        });
        var physical = delivery.Add(f.Connection);
        await f.Seed(65,2000,2);
        if(mode=="replay") { await TeleportConsumableTests.Send(f); await delivery.Settled(); }
        var originalEntry = mode=="replay" ? Entry(f) : null;
        var ordinal = originalEntry?.Ordinal;
        var before = physical.Sends.Count;
        var replacement = f.Connection with { }; var next = delivery.Add(replacement);
        if(mode=="scope") trace.Before = () => { f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId);f.Sender.Outbound.Connections.Add(replacement); };
        else f.Sender.Outbound.Encoder.Fail((connection,_) => { Assert.Same(f.Connection,connection); return trace.Fault; });
        var failure = await Record.ExceptionAsync(() => TeleportConsumableTests.Send(f).AsTask());
        Assert.NotNull(failure); Assert.True(Includes(failure,trace.Fault));
        var entry=Entry(f);
        if(originalEntry is not null){Assert.Same(originalEntry,entry);Assert.Equal(ordinal,entry.Ordinal);}
        Assert.Equal(EffectOperationState.Committed,entry.State);
        Assert.Equal(1,f.Count(2000)); Assert.Equal(11,f.Scalar("SELECT town_id FROM characters WHERE character_id=1"));
        if(mode=="replay" || Legacy) Assert.Same(trace.Fault,failure);
        else Assert.IsType<TransactionOutcomeUncertainException>(failure);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(1,physical.CloseCalls); Assert.Equal(0,next.CloseCalls); Assert.Empty(next.Events);
        Assert.Equal("close",physical.Events.Last().Kind);
        Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var chain));
        Assert.Equal(trace.Chain.Id,chain.Id);Assert.Equal(trace.Chain.RootId,chain.RootId);
        Assert.Equal(f.Connection.ConnectionId,chain.SourceConnection);Assert.Equal(GameplayCallSource.Connection,chain.Source);
        Assert.All(trace.Transactions,tx=>Assert.Equal(DomainTransactionCompletion.Committed,tx.Completion));Assert.NotEmpty(trace.Transactions);
        Assert.Equal(mode=="replay" || Legacy ? Array.Empty<SelectedSessionIdentity>() : new[]{f.Session},trace.Discards);
        // The old fine scope failed before the handler registered frames. The new whole scope
        // ends after its real success entries, which stay in their original order before close.
        Assert.Equal(mode=="scope" && !Legacy ? before+2 : before,physical.Sends.Count);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
        var blocked = f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection);
        var standing = f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session);
        if(mode!="replay" && !Legacy) { Assert.True(blocked); Assert.True(standing!.Fenced); }
        else { Assert.False(blocked); Assert.False(standing!.Fenced); }
        output.WriteLine($"mode={mode} legacy={Legacy} physicalSends={physical.Sends.Count} blocked={blocked} cause={failure}");
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Real_action_and_deferred_foreign_key_commit_failure_roll_back_all_assets(bool native)
    {
        using var f=TeleportConsumableTests.Fixture(); await f.Seed(65,2000,2);
        f.Sql(native ? "CREATE TABLE task41_parent(id INTEGER PRIMARY KEY); CREATE TABLE task41_child(id INTEGER REFERENCES task41_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER task41_fail AFTER UPDATE OF town_id ON characters BEGIN INSERT INTO task41_child VALUES(404); END" : "CREATE TRIGGER task41_fail BEFORE UPDATE OF town_id ON characters BEGIN SELECT RAISE(ABORT,'TASK41 action'); END");
        var failure=await Assert.ThrowsAsync<SqliteException>(()=>TeleportConsumableTests.Send(f).AsTask());
        Assert.Equal(native?787:1811,failure.SqliteExtendedErrorCode); if(native) Assert.Contains("SqliteTransaction.Commit",failure.StackTrace!);
        Assert.Equal(2,f.Count(2000)); Assert.Equal(0,f.Scalar("SELECT town_id FROM characters WHERE character_id=1")); Assert.Empty(f.Sender.Messages);
        TestGameplay.Run(f.Services,()=> { Assert.Equal((byte)0,f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().PositionOf(f.Session)!.TownId); Assert.False(f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().IsCoolingDown(f.Session.CharacterId,ItemCooldownKey.ForItem(2000))); });
        f.Sql("DROP TRIGGER task41_fail"); await TeleportConsumableTests.Send(f); Assert.Equal(1,f.Count(2000));
    }
    private static EffectOperationEntry Entry(ShopFixture f)
    {
        var table=typeof(EffectOperationLedger).GetField("ledgers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Services.GetRequiredService<EffectOperationLedger>())!;
        object?[] args=[f.Connection,null];Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,args)!);
        return Assert.Single(((IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!).Values.Cast<EffectOperationEntry>());
    }
    private static bool Includes(Exception outer,Exception cause)=>ReferenceEquals(outer,cause)||outer is AggregateException a&&a.InnerExceptions.Any(e=>Includes(e,cause))||outer.InnerException is {} inner&&Includes(inner,cause);
    public sealed class Trace
    { internal Exception Fault=new IOException("TASK41 real boundary"); internal Action? Before; internal GameplayCallChain Chain; internal List<DomainTransaction> Transactions=[]; internal List<SelectedSessionIdentity> Discards=[]; }
    private static void Decorate<T>(IServiceCollection services,Trace trace)where T:class
    {
        var d=services.Last(x=>x.ServiceType==typeof(T));
        services.AddSingleton<T>(p=>Observer<T>.Wrap((T)(d.ImplementationInstance??d.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,d.ImplementationType!)),p,trace));
    }
    public class Observer<T>:DispatchProxy where T:class
    {
        private T actual=null!;private IServiceProvider provider=null!;private Trace trace=null!;
        internal static T Wrap(T actual,IServiceProvider provider,Trace trace){var proxy=Create<T,Observer<T>>();var observer=(Observer<T>)(object)proxy;observer.actual=actual;observer.provider=provider;observer.trace=trace;return proxy;}
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(method!.Name is "UseAsync" or "ReadAsync")
            {
                trace.Chain=GameplayJob.Current!.Value;
                if(provider.GetRequiredService<IDomainTransactionAccessor>().Current is DomainTransaction tx&&!trace.Transactions.Contains(tx))trace.Transactions.Add(tx);
            }
            if(method.Name=="DiscardExact")trace.Discards.Add((SelectedSessionIdentity)args![0]!);
            try{return method.Invoke(actual,args);}catch(TargetInvocationException e)when(e.InnerException is {} cause){ExceptionDispatchInfo.Capture(cause).Throw();throw;}
        }
    }
    public sealed class ScopeFault<TCommand,TResult>(object actual,Trace trace):ICommandHandler<TCommand,TResult>,IDisposable where TCommand:class,ICommand<TResult> where TResult:notnull
    { public ValueTask<TResult> HandleAsync(CommandExecutionContext context,TCommand command,CancellationToken ct)=>((ICommandHandler<TCommand,TResult>)actual).HandleAsync(context,command,ct); public void Dispose() { trace.Before?.Invoke();throw trace.Fault; } }
    internal sealed class UnitTrace { internal int Main; }
    internal sealed class ObserveUnits(ICommandWorkUnits actual,UnitTrace trace):ICommandWorkUnits
    {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull
        { if(d.Kind==CommandWorkUnitKind.MainSettlement)trace.Main++;return actual.WriteAsync(c,d,a,ct); }
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>actual.WriteResultAsync(c,d,a,ct);
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>actual.ReadOnlyAsync(c,d,a,ct);
    }
}
