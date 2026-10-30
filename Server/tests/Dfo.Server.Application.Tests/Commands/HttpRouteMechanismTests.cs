using System.Reflection;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Execution;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Commands;

public sealed class HttpRouteMechanismTests
{
    [Theory]
    [InlineData("cera", false)] [InlineData("mail", false)]
    [InlineData("audit-action", false)] [InlineData("self-abort", false)]
    [InlineData("scope", true)] [InlineData("scope-same", true)]
    [InlineData("ordinary-write", true)] [InlineData("audit-cleanup", true)]
    [InlineData("rollback", true)] [InlineData("fake-completion", true)]
    public async Task Fixed_audit_preserves_failure_and_only_a_complete_allowed_occurrence_graph_carries_the_http_result(string mode, bool defaultFailure)
    {
        var state = new ProbeState { Mode = mode };
        await using var world = World(state);
        state.Services = world.Services;
        var command = Command(mode is "mail" or "self-abort" or "fake-completion");
        var failure = await Record.ExceptionAsync(() => world.Bus.SendInternalAsync(command, Origin()).AsTask());
        Assert.NotNull(failure);
        Assert.True(GameplayJob.EndedAJob(failure));
        Assert.Single(state.Log.Entries, item => item.Level == LogLevel.Error);
        Assert.True(Assert.Single(state.Resources).Disposed);
        Assert.True(state.Context!.Request!.IsClosed);
        Assert.NotNull(state.Context.Request.Failure);
        Assert.Equal(0, state.IllicitWrites);
        Assert.Equal(mode is "rollback" or "fake-completion" ? 0 : 1, state.AuditCalls);
        if (defaultFailure) Assert.IsNotType<CommandHttpAuditedFailureException>(failure);
        else
        {
            var carrier = Assert.IsType<CommandHttpAuditedFailureException>(failure);
            Assert.Equal(mode == "audit-action" ? "auditFailed" : mode == "self-abort" ? "rolledBack" : "unknown", carrier.Reply.Outcome);
            Assert.True(Contains(carrier, state.Cause));
            Assert.Equal(mode == "self-abort" ? "RolledBack" : "Unknown", state.AuditOutcome);
        }
        if (mode == "self-abort")
        {
            Assert.NotNull(state.Transaction!.ActionAbort);
            Assert.Same(state.Cause, state.Transaction.ActionAbort.Output);
            Assert.Null(state.Transaction.ActionRollback); // No Self capability or observation was granted.
        }
        // A new request has a new scope and request identity after either outcome.
        state.Mode = "ok";
        await world.Bus.SendInternalAsync(new HttpRouteCommand(HttpRouteOperation.Overview, new HttpRouteInput.Empty()), Origin());
        Assert.Equal(2, state.Resources.Count);
        Assert.NotSame(state.Resources[0], state.Resources[1]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_http_readonly_action_cancellation_is_marshaled_only_after_healthy_scope_cleanup(bool failScope)
    {
        using var producer = new CancellationTokenSource(); producer.Cancel();
        var state = new ProbeState { Mode = failScope ? "ro-scope" : "ro", Cause = new OperationCanceledException("body", producer.Token) };
        await using var world = World(state); state.Services = world.Services;
        var failure = await Record.ExceptionAsync(() => world.Bus.SendInternalAsync(
            new HttpRouteCommand(HttpRouteOperation.Events, new HttpRouteInput.Session("secret")), Origin(), producer.Token).AsTask());
        if (!failScope) Assert.Same(state.Cause, failure);
        else Assert.NotNull(failure);
        Assert.Equal(failScope, GameplayJob.EndedAJob(failure!));
        Assert.Equal(failScope ? 1 : 0, state.Log.Entries.Count(x => x.Level == LogLevel.Error));
        Assert.True(state.Context!.Request!.IsClosed);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, state.Transaction!.Completion);
        Assert.True(Assert.Single(state.Resources).Disposed);
    }

    [Theory]
    [InlineData("wrong-kind")] [InlineData("unknown-operation")] [InlineData("wrong-input")] [InlineData("wrong-query")]
    public async Task Invalid_source_or_closed_operation_shape_is_a_failed_command_before_any_domain_action(string mode)
    {
        var state = new ProbeState(); await using var world = World(state); state.Services = world.Services;
        var command = mode switch
        {
            "unknown-operation" => new HttpRouteCommand((HttpRouteOperation)999, new HttpRouteInput.Empty()),
            "wrong-query" => new HttpRouteCommand(HttpRouteOperation.CeraAccounts, new HttpRouteInput.Cera("secret", GmCeraQuery.Normalize, new())),
            _ => new HttpRouteCommand(HttpRouteOperation.Overview, mode == "wrong-input" ? new HttpRouteInput.Session("secret") : new HttpRouteInput.Empty())
        };
        var origin = mode == "wrong-kind" ? new InternalCommandOrigin(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer), "same-label") : Origin();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Bus.SendInternalAsync(command, origin).AsTask());
        Assert.True(GameplayJob.EndedAJob(error)); Assert.Equal(0, state.DomainCalls);
    }

    [Fact]
    public async Task A_closed_request_audit_guard_poisons_the_current_job_even_when_the_caller_catches_it()
    {
        var state = new ProbeState { Mode = "cera" }; await using var world = World(state); state.Services = world.Services;
        await Assert.ThrowsAsync<CommandHttpAuditedFailureException>(() => world.Bus.SendInternalAsync(Command(false), Origin()).AsTask());
        var closed = state.Context!;
        world.Settings.Action = async context =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => world.Services.GetRequiredService<ICommandHttpFailureAudit>().RecordAsync(closed, state.Cause, default).AsTask());
            return 1;
        };
        var error = await Record.ExceptionAsync(() => world.Internal(new ProtocolCommandExecutionTests.InternalProbe()).AsTask());
        Assert.NotNull(error); Assert.True(GameplayJob.EndedAJob(error));
    }

    private static bool Contains(Exception error, Exception cause) => ReferenceEquals(error, cause)
        || error is AggregateException aggregate && aggregate.InnerExceptions.Any(x => Contains(x, cause))
        || error.InnerException is { } inner && Contains(inner, cause);
    private static InternalCommandOrigin Origin() => new(GameplayCallSource.Gm, GameplayCallChain.NewRoot(GameplayCallSource.Gm), "gm:POST /probe", InternalCommandCancellation.BodyOnly);
    private static HttpRouteCommand Command(bool isMail) => isMail
        ? new(HttpRouteOperation.MailSend, new HttpRouteInput.MailSendRequest("secret", new("title", "body", [1], [], 0, RequestId: Guid.NewGuid())))
        : new(HttpRouteOperation.CeraGrant, new HttpRouteInput.CeraGrantRequest("secret", new([1], "Cera", 1, "reason", Guid.NewGuid())));
    private static ProtocolCommandExecutionTests.CommandWorld World(ProbeState state) => new(configure: services =>
    {
        services.AddSingleton<ILogger<GameplayExecutor>>(state.Log);
        foreach (var type in new[] { typeof(IAuthenticationDomainService), typeof(IGmAccessDomainService), typeof(IGmAuditDomainService), typeof(IGmPlayerDirectoryDomainService),
            typeof(IGmCeraDomainService), typeof(IGmCeraReminderDomainService), typeof(IGmOfficialMailDomainService), typeof(IGmInitializationDomainService), typeof(IGmDailyRefillDomainService),
            typeof(IGmEventsDomainService), typeof(IGmJoustDomainService) })
        {
            var proxy = DispatchProxy.Create(type, typeof(DomainProxy)); ((DomainProxy)proxy).State = state;
            services.AddSingleton(type, proxy);
        }
        services.AddInternalCommandHandler<HttpRouteCommand, HttpRouteReply, HttpRouteCommandHandler>(ReadOnlyActionCancellationCapability.ConfirmedRollbackRead);
        services.AddScoped<HttpRouteCommandHandler>();
        services.AddScoped<ICommandHandler<HttpRouteCommand, HttpRouteReply>>(provider =>
            new ObservingHandler(provider.GetRequiredService<HttpRouteCommandHandler>(), provider.GetRequiredService<ICommandWorkUnits>(), state, new Resource(state)));
    });

    internal sealed class ProbeState
    {
        internal string Mode = "ok";
        internal Exception Cause = new IOException("actual operation fault");
        internal ServiceProvider Services = null!;
        internal CommandExecutionContext? Context;
        internal DomainTransaction? Transaction;
        internal int DomainCalls, AuditCalls, IllicitWrites;
        internal string? AuditOutcome;
        internal List<Resource> Resources = [];
        internal RecordingExecutionLogger<GameplayExecutor> Log = new();
    }
    internal sealed class Resource : IAsyncDisposable
    {
        internal bool Disposed;
        internal Exception? Failure;
        internal Resource(ProbeState state) { state.Resources.Add(this); }
        public ValueTask DisposeAsync() { Disposed = true; return Failure is { } error ? ValueTask.FromException(error) : ValueTask.CompletedTask; }
    }
    private sealed class ObservingHandler(HttpRouteCommandHandler inner, ICommandWorkUnits units, ProbeState state, Resource resource)
        : ICommandHandler<HttpRouteCommand, HttpRouteReply>, IAsyncDisposable
    {
        public async ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, HttpRouteCommand command, CancellationToken ct)
        {
            state.Context = context;
            if (state.Mode == "scope") resource.Failure = new IOException("independent scope cleanup");
            if (state.Mode == "ro-scope") resource.Failure = new OperationCanceledException("independent scope cancellation");
            if (state.Mode == "scope-same") resource.Failure = state.Cause;
            try { return await inner.HandleAsync(context, command, ct); }
            catch
            {
                if (state.Mode == "ordinary-write")
                    await Assert.ThrowsAnyAsync<Exception>(() => units.WriteAsync(context, new("forbidden-after-audit", []), (_, _) => { state.IllicitWrites++; return ValueTask.FromResult(1); }).AsTask());
                throw;
            }
        }
        public ValueTask DisposeAsync() => resource.DisposeAsync();
    }
    public class DomainProxy : DispatchProxy
    {
        private ProbeState state = null!;
        internal ProbeState State { set => state = value; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            state.DomainCalls++;
            var current = state.Services.GetRequiredService<IDomainTransactionAccessor>().Current;
            switch (method!.Name)
            {
                case "OverviewAsync": return ValueTask.FromResult(new GmOverview(false, false, 0, 0, 0, 0, [], 0, DateTimeOffset.UtcNow));
                case "QueryAsync" when method.DeclaringType == typeof(IGmCeraDomainService): return ValueTask.FromResult(new GmCeraQueryResult(args![4]!));
                case "QueryAsync" when method.DeclaringType == typeof(IGmOfficialMailDomainService):
                    return ValueTask.FromResult(new GmMailQueryResult(new GmMailPreparation((Guid)args![3]!, (GmMailInput)args[4]!, [new AccountId(1)])));
                case "GrantAsync":
                    state.Transaction = (DomainTransaction)current!;
                    state.Transaction.Enlist(new object(), () => new Participant(state, state.Mode == "rollback", false));
                    return ValueTask.FromResult(new GmCeraGrantResult(new(Guid.NewGuid(), 1, "admin", DateTimeOffset.UtcNow, new([1], "Cera", 1, "reason"), []), false));
                case "SendAsync": return SendMail(current!);
                case "RecordFailureAsync":
                    state.AuditCalls++; state.AuditOutcome = (string)args![2]!;
                    if (state.Mode == "audit-cleanup") current!.Enlist(new object(), () => new Participant(state, false, true));
                    if (state.Mode is "audit-action" or "audit-cleanup") return ValueTask.FromException<bool>(new IOException("audit storage unavailable"));
                    return ValueTask.FromResult(true);
                case "ListAsync":
                    state.Transaction = (DomainTransaction)current!;
                    return ValueTask.FromException<ManagedEventList>(state.Cause);
                default: throw new InvalidOperationException("Unexpected domain method " + method.Name);
            }
        }
        private async ValueTask<GmMailSendResult> SendMail(IDomainTransaction current)
        {
            state.Transaction = (DomainTransaction)current;
            if (state.Mode == "self-abort") { await state.Transaction.AbortActionAsync(state.Cause); throw state.Cause; }
            if (state.Mode == "fake-completion") { await state.Transaction.AbortAsync(state.Cause); throw state.Cause; }
            current.Enlist(new object(), () => new Participant(state, false, false));
            return new(new(Guid.NewGuid(), 1, "admin", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), new("t", "b", [1], [], 0), []), false);
        }
    }
    private sealed class Participant(ProbeState state, bool fallible, bool rollbackFailure) : ITransactionParticipant
    {
        public bool CommitCanFail => fallible;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.FromException(state.Cause);
        public ValueTask RollbackAsync(CancellationToken ct) => rollbackFailure || state.Mode == "rollback" ? ValueTask.FromException(new IOException("independent rollback failure")) : ValueTask.CompletedTask;
    }
}
