using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.Mailbox;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Mail;

public sealed class MailboxProtocolCommandFlowTests
{
    [Theory]
    [InlineData("action")]
    [InlineData("native")]
    [InlineData("rollback")]
    [InlineData("nonfallible")]
    public async Task Real_SQL_sources_preserve_all_tables_original_causes_and_precise_completion(string mode)
    {
        var trace = new Task31MailTrace { Mode = mode };
        using var f = Fixture(trace);
        await f.Seed(121, 1000, 20, 3); await f.Seed(9, 3000, 0, 1);
        f.Sql(mode == "native"
            ? "CREATE TABLE t31_parent(id INTEGER PRIMARY KEY);CREATE TABLE t31_child(id INTEGER REFERENCES t31_parent(id) DEFERRABLE INITIALLY DEFERRED);CREATE TRIGGER t31_fault AFTER INSERT ON mailbox_messages BEGIN INSERT INTO t31_child VALUES(404);END;"
            : mode == "nonfallible" ? "SELECT 1"
            : "CREATE TRIGGER t31_fault BEFORE INSERT ON mailbox_messages BEGIN SELECT RAISE(ABORT,'task31 real action fault');END;");
        var before = Task31Assets.Read(f.Services);
        Exception? error = await Record.ExceptionAsync(() => MailboxTestWire.Dispatch(f, Send()));
        Assert.NotNull(trace.Transaction);
        Assert.Null(f.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
        if (mode is "rollback" or "nonfallible")
        {
            Assert.NotNull(error); Assert.True(TransactionOutcomeUncertainException.Contains(error));
            Assert.True(CommandSelfSettledWorkUnitsTests.Has(error, trace.Second));
            Assert.Equal(DomainTransactionCompletion.Uncertain, trace.Transaction.Completion);
            Assert.Empty(f.Sender.Messages);
            var close = Assert.Single(f.Sender.Outbound.Entries); Assert.True(close.IsClose); Assert.Same(f.Connection, close.Connection);
        }
        else
        {
            Assert.Null(error); var ack = Assert.IsType<MailboxSendResponse>(Assert.Single(f.Sender.Messages)); Assert.Equal(3, ack.Error);
            Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, trace.Transaction.Completion);
            Assert.DoesNotContain(f.Sender.Outbound.Entries, entry => entry.IsClose);
        }
        if (mode != "nonfallible") Assert.Equal(before, Task31Assets.Read(f.Services));
        else { Assert.Equal(99970, f.Gold); Assert.Single(await f.Services.GetRequiredService<IMailboxDomainService>().InboxAsync(new(2))); }
        if (mode == "native")
        {
            var fact = Assert.IsType<TransactionFailureFact>(trace.Transaction.CommitFailure);
            var cause = Assert.IsType<SqliteException>(fact.Cause);
            Assert.Equal(787, cause.SqliteExtendedErrorCode); Assert.Equal(TransactionFailurePhase.FallibleCommit, fact.Phase);
            Assert.Equal(TransactionFailureKind.KnownStorage, fact.Kind); Assert.Null(trace.Transaction.ActionRollback);
        }
        else if (mode != "nonfallible")
        {
            var fact = trace.Transaction.ActionRollback!; var sql = Assert.IsType<SqliteException>(fact.Cause);
            Assert.Equal(1811, sql.SqliteExtendedErrorCode); Assert.Same(trace.DomainFailure, fact.Output);
            if (error is not null) Assert.True(CommandSelfSettledWorkUnitsTests.Has(error, sql));
            if (mode == "rollback")
            {
                Assert.Same(fact.Output, error);
                var typed = Assert.IsType<TransactionOutcomeUncertainException>(error);
                Assert.Equal(TransactionUncertaintyReason.RollbackFailure, typed.Reason);
                Assert.True(CommandSelfSettledWorkUnitsTests.Has(typed, sql));
                Assert.True(CommandSelfSettledWorkUnitsTests.Has(typed, trace.Second));
                var failures = Assert.IsAssignableFrom<IEnumerable<(object Source, Exception Error)>>(typeof(CommandRequestContext)
                    .GetField("failures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(trace.Request));
                var occurrence = Assert.Single(failures);
                Assert.Same(fact.Occurrence, occurrence.Source); Assert.Same(fact.Output, occurrence.Error);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lower_domain_catch_cannot_register_or_commit_a_second_transaction_after_real_inner_abort(bool begin)
    {
        var trace = new Task31MailTrace { Mode = begin ? "begin" : "register" }; using var f = Fixture(trace);
        await f.Seed(121, 1000, 20, 3); await f.Seed(9, 3000, 0, 1);
        f.Sql("CREATE TRIGGER t31_fault BEFORE INSERT ON mailbox_messages BEGIN SELECT RAISE(ABORT,'task31 lower-window');END;");
        var before = Task31Assets.Read(f.Services);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => MailboxTestWire.Dispatch(f, Send()));
        Assert.NotNull(trace.AdmissionFailure); Assert.NotNull(trace.DomainFailure);
        Assert.True(CommandSelfSettledWorkUnitsTests.Has(error, trace.DomainFailure));
        Assert.True(CommandSelfSettledWorkUnitsTests.Has(error, trace.AdmissionFailure));
        Assert.Equal(0, trace.SecondWrites); Assert.Equal(0, trace.SecondCommits);
        Assert.Equal(before, Task31Assets.Read(f.Services));
        Assert.Empty(f.Sender.Messages); Assert.True(Assert.Single(f.Sender.Outbound.Entries).IsClose);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, trace.Transaction!.Completion);
        Assert.NotEmpty(trace.Transaction.OutboxUnit!.SelfSettlementGuards);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Whole_scope_failure_retains_preexisting_success_or_refusal_frames_and_then_closes_source(bool refusal)
    {
        var trace = new Task31MailTrace { Mode = "scope" }; using var f = Fixture(trace);
        var request = refusal ? new MailboxQueryRequest(GameMessageMetadata.Empty, Encoding.UTF8.GetBytes("missing")) as IClientNetworkMessage
            : new MailboxOpenRequest(GameMessageMetadata.Empty);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => MailboxTestWire.Dispatch(f, request));
        Assert.True(CommandSelfSettledWorkUnitsTests.Has(error, trace.Second));
        Assert.Equal(DomainTransactionCompletion.Committed, trace.ScopeTransaction!.Completion);
        Assert.True(trace.ScopeDisposed);
        Assert.Equal(refusal ? 1 : 2, f.Sender.Messages.Count);
        if (refusal) Assert.Equal(21, Assert.IsType<MailboxQueryResponse>(f.Sender.Messages[0]).Error);
        else Assert.Equal(0, Assert.IsType<MailboxOpenResponse>(f.Sender.Messages[0]).Error);
        Assert.True(f.Sender.Outbound.Entries.Last().IsClose); Assert.Same(f.Connection, f.Sender.Outbound.Entries.Last().Connection);
    }

    [Fact]
    public async Task An_unawaited_direct_domain_observation_is_drained_before_abort_and_never_commits_staged_SQL()
    {
        var trace = new Task31MailTrace { Mode = "unawaited" }; using var f = Fixture(trace);
        var before = Task31Assets.Read(f.Services);
        var pending = f.Services.GetRequiredService<ICommandBus>().SendAsync(new EarlyCommand(new(f.Connection), new EarlyMessage())).AsTask();
        await trace.DomainReturned.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(100);
        var early = pending.IsCompleted;
        trace.ReleaseDomain.TrySetResult();
        var error = await Record.ExceptionAsync(async () => await pending);
        Assert.False(early); Assert.NotNull(error);
        Assert.Equal(before, Task31Assets.Read(f.Services));
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, trace.Transaction!.Completion);
        Assert.Empty(f.Sender.Messages); Assert.True(Assert.Single(f.Sender.Outbound.Entries).IsClose);
    }
    [Theory]
    [InlineData("completed-sync")]
    [InlineData("completed-before-return")]
    public async Task A_discarded_completed_domain_fault_cannot_commit_real_staged_SQL(string mode)
    {
        var trace = new Task31MailTrace { Mode = mode }; using var f = Fixture(trace);
        var before = Task31Assets.Read(f.Services);
        var error = await Record.ExceptionAsync(async () => await f.Services.GetRequiredService<ICommandBus>()
            .SendAsync(new EarlyCommand(new(f.Connection), new EarlyMessage())));
        Assert.True(trace.StagedMails > 0); Assert.True(trace.ObservationFaultedBeforeReturn);
        Assert.True(error is not null, $"Discarded completed fault was committed: mode={mode}; completion={trace.Transaction!.Completion}; stagedMails={trace.StagedMails}; durableChanged={before != Task31Assets.Read(f.Services)}");
        Assert.True(CommandSelfSettledWorkUnitsTests.Has(error, trace.Second));
        Assert.Equal(before, Task31Assets.Read(f.Services));
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, trace.Transaction!.Completion);
        Assert.Null(trace.Transaction.ActionRollback); Assert.Null(trace.Transaction.CommitFailure);
        Assert.Empty(f.Sender.Messages);
        var close = Assert.Single(f.Sender.Outbound.Entries); Assert.True(close.IsClose); Assert.Same(f.Connection, close.Connection);
    }

    [Fact]
    public async Task A_properly_awaited_plain_domain_fault_can_be_accepted_by_its_explicit_local_rollback()
    {
        var trace = new Task31MailTrace { Mode = "awaited-refusal" }; using var f = Fixture(trace);
        var before = Task31Assets.Read(f.Services);
        var result = await f.Services.GetRequiredService<ICommandBus>().SendAsync(new EarlyCommand(new(f.Connection), new EarlyMessage()));
        Assert.NotNull(result); Assert.True(trace.StagedMails > 0); Assert.Same(trace.Second, trace.AcceptedRollbackCause);
        Assert.Equal(before, Task31Assets.Read(f.Services));
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, trace.Transaction!.Completion);
        Assert.Null(trace.Transaction.ActionRollback); Assert.Null(trace.Transaction.CommitFailure);
        Assert.Empty(f.Sender.Messages); Assert.Empty(f.Sender.Outbound.Entries);
    }

    public sealed record EarlyMessage : IClientNetworkMessage
    { public ClientMessageType MessageType => (ClientMessageType)0x7F000001; }
    public sealed record EarlyCommand(NetworkDispatchContext Dispatch, IClientNetworkMessage Message) : IProtocolCommand<MailboxResult>
    { public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(GameMessageMetadata.Empty); }
    public sealed class EarlyHandler(IMailboxDomainService mailbox, ICommandWorkUnits units, Task31MailTrace trace) : ICommandHandler<EarlyCommand, MailboxResult>
    {
        public async ValueTask<MailboxResult> HandleAsync(CommandExecutionContext context, EarlyCommand command, CancellationToken ct)
        {
            var result = await units.WriteSelfSettledResultAsync(context, new("unawaited-direct", []), async (unit, token) =>
            {
                var operation = unit.InvokeSelfSettlingAsync(t => mailbox.SendAsync(MailBuilder.FromSystem(new(2)).AddGold(5).Build(), t), token).AsTask();
                if (trace.Mode == "completed-sync")
                    Assert.True(operation.IsFaulted, "The real staged domain/proxy fault must complete synchronously for this timing control.");
                else if (trace.Mode == "completed-before-return")
                {
                    Assert.False(operation.IsCompleted);
                    _ = operation.ContinueWith(_ => trace.ObservationCompleted.TrySetResult(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    trace.ReleaseDomain.TrySetResult();
                    await trace.ObservationCompleted.Task;
                    Assert.True(operation.IsFaulted);
                }
                else if (trace.Mode == "awaited-refusal")
                {
                    try { await operation; }
                    catch (Exception cause) { unit.RequireRollback(cause); trace.AcceptedRollbackCause = cause; }
                }
                trace.ObservationFaultedBeforeReturn = operation.IsFaulted;
                return new MailboxResult();
            }, ct);
            return ((CommandSelfSettledWriteResult<MailboxResult>.Returned)result).Value;
        }
    }

    private static MailboxSendRequest Send() => new(GameMessageMetadata.Empty, true,
        new(Encoding.UTF8.GetBytes("shop2"), 30, "whole assets", [new(new(BusinessInventorySpace.CarriedEquipment, 0), 3000, 1), new(new(BusinessInventorySpace.Material, 0), 1000, 3)]));
    private static ShopFixture Fixture(Task31MailTrace trace) => new(services =>
    {
        services.AddSingleton<IMailItemDefinitionSource>(new FreeMail());
        services.AddSingleton(trace);
        if (trace.Mode is "unawaited" or "completed-sync" or "completed-before-return" or "awaited-refusal") services.AddProtocolCommandHandler<EarlyCommand, MailboxResult, EarlyHandler>(CommandSelfSettlementCapability.SelfSettledAction,
            new ProtocolCommandMessageBinding((ClientMessageType)0x7F000001, typeof(EarlyMessage)));
        Task31DomainProxy.Decorate<IMailboxDomainService>(services, trace);
        var capturedWhole = services.Last(d => d.ServiceType == typeof(ICommandHandler<GameMailboxCommand, GameMailboxOutcome>));
        services.AddScoped<ICommandHandler<GameMailboxCommand, GameMailboxOutcome>>(provider => new CapturedWhole(
            (ICommandHandler<GameMailboxCommand, GameMailboxOutcome>)(capturedWhole.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, capturedWhole.ImplementationType!)), trace));
        if (trace.Mode == "scope")
        {
            services.AddScoped(provider => new Task31ScopeFailure(provider.GetRequiredService<IDomainTransactionAccessor>(), trace));
            var unitDescriptor = services.Last(d => d.ServiceType == typeof(ICommandWorkUnits));
            services.AddSingleton<ICommandWorkUnits>(provider => new Task31ObservedUnits((ICommandWorkUnits)(unitDescriptor.ImplementationInstance
                ?? unitDescriptor.ImplementationFactory?.Invoke(provider) ?? ActivatorUtilities.CreateInstance(provider, unitDescriptor.ImplementationType!)), provider, trace));
            var original = services.Last(d => d.ServiceType == typeof(ICommandHandler<GameMailboxCommand, GameMailboxOutcome>));
            services.AddScoped<ICommandHandler<GameMailboxCommand, GameMailboxOutcome>>(provider =>
                new ScopedWhole((ICommandHandler<GameMailboxCommand, GameMailboxOutcome>)(original.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)), provider.GetRequiredService<Task31ScopeFailure>()));
        }
    });
    private sealed class CapturedWhole(ICommandHandler<GameMailboxCommand, GameMailboxOutcome> inner, Task31MailTrace trace)
        : ICommandHandler<GameMailboxCommand, GameMailboxOutcome>
    {
        public ValueTask<GameMailboxOutcome> HandleAsync(CommandExecutionContext context, GameMailboxCommand command, CancellationToken ct)
        { trace.Request = context.Request; return inner.HandleAsync(context, command, ct); }
    }
    private sealed class FreeMail : IMailItemDefinitionSource
    { public ValueTask<MailItemDefinition?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<MailItemDefinition?>(new(MailItemAttachType.Free, "free", null)); }
    private sealed class ScopedWhole(ICommandHandler<GameMailboxCommand, GameMailboxOutcome> inner, Task31ScopeFailure resource)
        : ICommandHandler<GameMailboxCommand, GameMailboxOutcome>
    {
        public async ValueTask<GameMailboxOutcome> HandleAsync(CommandExecutionContext context, GameMailboxCommand command, CancellationToken ct)
        {
            resource.Before(); return await inner.HandleAsync(context, command, ct);
        }
    }
}

public sealed class Task31MailTrace
{
    internal string Mode = "";
    internal DomainTransaction? Transaction;
    internal DomainTransaction? ScopeTransaction;
    internal CommandRequestContext? Request;
    internal Exception? DomainFailure, AdmissionFailure;
    internal readonly IOException Second = new("task31 independent settlement failure");
    internal int SecondWrites, SecondCommits;
    internal readonly TaskCompletionSource DomainReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource ReleaseDomain = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool ScopeDisposed, ScopeResolved;
    internal int StagedMails;
    internal bool ObservationFaultedBeforeReturn;
    internal Exception? AcceptedRollbackCause;
    internal readonly TaskCompletionSource ObservationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public class Task31DomainProxy : DispatchProxy
{
    private object inner = null!; private IServiceProvider services = null!; private Task31MailTrace trace = null!;
    internal static void Decorate<T>(IServiceCollection services, Task31MailTrace trace) where T : class
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(T));
        services.AddSingleton<T>(provider =>
        {
            var original = (T)(descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
            var proxy = Create<T, Task31DomainProxy>(); var owner = (Task31DomainProxy)(object)proxy;
            owner.inner = original; owner.services = provider; owner.trace = trace; return proxy;
        });
    }
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method!.ReturnType == typeof(ValueTask)) return Void(method, arguments!);
        if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            return GetType().BaseType!.GetMethod(nameof(Generic), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(method.ReturnType.GenericTypeArguments[0]).Invoke(this, [method, arguments]);
        return Call(method, arguments!);
    }
    private object? Call(MethodInfo method, object?[] arguments)
    {
        try { return method.Invoke(inner, arguments); }
        catch (TargetInvocationException error) when (error.InnerException is { } cause) { ExceptionDispatchInfo.Throw(cause); throw; }
    }
    private async ValueTask Void(MethodInfo method, object?[] arguments)
    {
        if (method.Name == "AcquireWriteAsync" && services.GetRequiredService<IDomainTransactionAccessor>().Current is DomainTransaction tx)
        {
            trace.Transaction = tx;
            if (trace.Mode == "rollback") tx.Enlist(trace, () => new Task31SecondFailure(trace));
        }
        await (ValueTask)Call(method, arguments)!;
    }
    private async ValueTask<T> Generic<T>(MethodInfo method, object?[] arguments)
    {
        var current = services.GetRequiredService<IDomainTransactionAccessor>().Current as DomainTransaction;
        if (method.Name is "SendPreparedAsync" or "SendAsync" or "ClaimAsync" && current is not null)
        {
            trace.Transaction = current;
            if (trace.Mode is "rollback" or "nonfallible") current.Enlist(trace, () => new Task31SecondFailure(trace));
        }
        try
        {
            var result = await (ValueTask<T>)Call(method, arguments)!;
            if (method.Name == "SendAsync" && trace.Mode is "unawaited" or "completed-sync" or "completed-before-return" or "awaited-refusal")
            {
                trace.StagedMails = (await ((IMailboxDomainService)inner).InboxAsync(new(2))).Count;
                Assert.True(trace.StagedMails > 0); Assert.Equal(DomainTransactionCompletion.Open, trace.Transaction!.Completion);
                if (trace.Mode is "unawaited" or "completed-before-return")
                { trace.DomainReturned.TrySetResult(); await trace.ReleaseDomain.Task; }
                if (trace.Mode != "unawaited") throw trace.Second;
            }
            return result;
        }
        catch (Exception cause)
        {
            trace.DomainFailure = cause;
            if (trace.Mode is "register" or "begin" && trace.Transaction?.ActionRollback is { })
            {
                Assert.Null(services.GetRequiredService<IDomainTransactionAccessor>().Current);
                try
                {
                    if (trace.Mode == "register") services.GetRequiredService<GameplayOutboxes>().Current.RequestClose(new(new("forbidden"), NetworkEndpointKind.Game, 1));
                    else
                    {
                        await using var second = services.GetRequiredService<DomainTransactionScope>().Begin();
                        trace.SecondWrites++;
                        await services.GetRequiredService<SqliteDatabase>().WriteAsync(async (sql, ct) =>
                        { sql.CommandText = "UPDATE accounts SET cera=cera+1 WHERE account_id=1"; await sql.ExecuteNonQueryAsync(ct); }, default);
                        trace.SecondCommits++; await second.CommitAsync(default);
                    }
                }
                catch (Exception guard) { trace.AdmissionFailure = guard; }
            }
            throw;
        }
    }
}
internal sealed class Task31SecondFailure(Task31MailTrace trace) : ITransactionParticipant
{
    public bool CommitCanFail => false;
    public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
    public ValueTask CommitAsync(CancellationToken ct) => trace.Mode == "nonfallible" ? ValueTask.FromException(trace.Second) : ValueTask.CompletedTask;
    public ValueTask RollbackAsync(CancellationToken ct) => trace.Mode == "rollback" ? ValueTask.FromException(trace.Second) : ValueTask.CompletedTask;
}
internal sealed class Task31ScopeFailure(IDomainTransactionAccessor accessor, Task31MailTrace trace) : IAsyncDisposable
{
    internal void Before() => trace.ScopeResolved = true;
    public ValueTask DisposeAsync()
    {
        Assert.True(trace.ScopeResolved);
        Assert.Null(accessor.Current);
        trace.ScopeDisposed = true;
        return ValueTask.FromException(trace.Second);
    }
}

internal sealed class Task31ObservedUnits(ICommandWorkUnits inner, IServiceProvider services, Task31MailTrace trace) : ICommandWorkUnits
{
    public ValueTask<CommandSelfSettledWriteResult<T>> WriteSelfSettledResultAsync<T>(CommandExecutionContext context,
        CommandWorkUnitDeclaration declaration, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull =>
        inner.WriteSelfSettledResultAsync(context, declaration, (unit, token) =>
        { trace.ScopeTransaction = (DomainTransaction)services.GetRequiredService<IDomainTransactionAccessor>().Current!; return action(unit, token); }, ct);
    public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull => inner.WriteResultAsync(context, declaration, action, ct);
    public ValueTask<T> WriteAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull => inner.WriteAsync(context, declaration, action, ct);
    public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull => inner.ReadOnlyAsync(context, declaration, action, ct);
}

internal static class Task31Assets
{
    internal static string Read(IServiceProvider services)
    {
        using var connection = new SqliteConnection(services.GetRequiredService<SqliteDatabase>().ConnectionString); connection.Open();
        using var schema = connection.CreateCommand(); schema.CommandText = "SELECT name,sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        var tables = new List<(string Name, string Schema)>(); using (var reader = schema.ExecuteReader()) while (reader.Read()) tables.Add((reader.GetString(0), reader.GetString(1)));
        var result = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            using var sql = connection.CreateCommand(); sql.CommandText = "SELECT * FROM \"" + table.Name.Replace("\"", "\"\"") + "\"";
            using var reader = sql.ExecuteReader(); var rows = new List<string>();
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            while (reader.Read()) rows.Add(JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i) switch
            { DBNull => new[] { "null", "" }, byte[] blob => new[] { "blob", Convert.ToHexString(blob) }, long value => new[] { "integer", value.ToString(CultureInfo.InvariantCulture) }, double value => new[] { "real", value.ToString("R", CultureInfo.InvariantCulture) }, string value => new[] { "text", value }, var value => throw new InvalidDataException(value.GetType().Name) })));
            rows.Sort(StringComparer.Ordinal); result[table.Name] = new { table.Schema, columns, rows };
        }
        return JsonSerializer.Serialize(result);
    }
}
