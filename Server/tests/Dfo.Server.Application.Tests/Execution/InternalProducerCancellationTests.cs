using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Tests.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Execution;

public sealed class InternalProducerCancellationTests
{
    [Theory]
    [InlineData("body", false)] [InlineData("body", true)]
    [InlineData("action", false)] [InlineData("action", true)]
    [InlineData("scope", false)] [InlineData("scope-same", false)]
    [InlineData("guard", false)] [InlineData("aggregate", false)]
    [InlineData("readonly", false)] [InlineData("result", false)]
    [InlineData("self", false)] [InlineData("lease", false)] [InlineData("action-rollback-same", false)]
    [InlineData("commit", false)] [InlineData("not-cancelled", false)]
    public async Task Only_the_actual_single_body_or_healthy_action_cancellation_leaves_the_job_without_error(string mode, bool differentExceptionToken)
    {
        using var producer = new CancellationTokenSource();
        if (mode != "not-cancelled") producer.Cancel();
        var cause = new OperationCanceledException("original action cancellation", differentExceptionToken ? CancellationToken.None : producer.Token);
        var cleanup = new OperationCanceledException("independent cleanup cancellation", producer.Token);
        var log = new RecordingExecutionLogger<GameplayExecutor>();
        await using var world = new ProtocolCommandExecutionTests.CommandWorld(configure: services => services.AddSingleton<ILogger<GameplayExecutor>>(log));
        var units = world.Services.GetRequiredService<ICommandWorkUnits>();
        var actionRan = false;
        DomainTransaction? transaction = null;
        var rollbackCalls = 0;
        world.Settings.Action = async context =>
        {
            if (mode is "scope" or "scope-same")
                world.Settings.Resources.Last().CleanupFailure = mode == "scope" ? cleanup : cause;
            if (mode == "scope") return 1;
            if (mode == "guard") GameplayJob.RecordProgramError(cause);
            if (mode == "aggregate") throw new AggregateException(cause);
            if (mode == "self")
            {
                await units.WriteSelfSettledResultAsync<int>(context, new("forbidden-self", []), (_, _) => ValueTask.FromResult(1), producer.Token);
                return 1;
            }
            if (mode is "lease" or "action-rollback-same")
                return await units.WriteAsync<int>(context, new("rollback-source", []), (unit, _) =>
                {
                    transaction = Assert.IsType<DomainTransaction>(world.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
                    transaction.Enlist(new object(), () => new RollbackCancellation(cause, () => rollbackCalls++));
                    if (mode == "lease") { unit.RequireRollback(); return ValueTask.FromResult(1); }
                    return ValueTask.FromException<int>(cause);
                }, producer.Token);
            if (mode == "commit")
                await units.WriteAsync(context, new("first-committed", []), (_, _) => ValueTask.FromResult(1), producer.Token);
            if (mode is "action" or "readonly" or "result")
            {
                if (mode == "readonly") return await units.ReadOnlyAsync<int>(context, new("query", []), _ => { actionRan = true; return ValueTask.FromException<int>(cause); }, producer.Token);
                if (mode == "result")
                {
                    await units.WriteResultAsync<int>(context, new("result", []), (_, _) => { actionRan = true; return ValueTask.FromException<int>(cause); }, producer.Token);
                    return 1;
                }
                return await units.WriteAsync<int>(context, new("action", []), (_, _) => { actionRan = true; transaction = Assert.IsType<DomainTransaction>(world.Services.GetRequiredService<IDomainTransactionAccessor>().Current); return ValueTask.FromException<int>(cause); }, producer.Token);
            }
            throw cause;
        };
        var failure = await Record.ExceptionAsync(() => world.Bus.SendInternalAsync(new ProtocolCommandExecutionTests.InternalProbe(),
            new(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer), "timer:cancel-provenance", InternalCommandCancellation.BodyOnly), producer.Token).AsTask());
        var pure = mode is "body" or "action";
        if (pure) Assert.Same(cause, failure);
        if (mode == "action")
        {
            Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, transaction!.Completion);
            Assert.True(transaction.IsSettled);
        }
        if (mode is "lease" or "action-rollback-same")
        {
            Assert.Equal(1, rollbackCalls);
            Assert.Equal(DomainTransactionCompletion.Uncertain, transaction!.Completion);
            var uncertain = Assert.IsType<TransactionOutcomeUncertainException>(failure);
            var causes = Assert.IsType<AggregateException>(uncertain.InnerException).InnerExceptions;
            Assert.Equal(mode == "lease" ? 1 : 2, causes.Count(error => ReferenceEquals(error, cause)));
        }
        if (mode == "commit") Assert.IsType<TransactionOutcomeUncertainException>(failure);
        else Assert.NotNull(failure);
        Assert.Equal(pure ? 0 : 1, log.Entries.Count(entry => entry.Level == LogLevel.Error));
        Assert.Equal(!pure, GameplayJob.EndedAJob(failure!));
        Assert.True(Assert.Single(world.Settings.Resources).Disposed);
        Assert.True(Assert.Single(world.Settings.Seen).Request!.IsClosed);
        if (mode is "action" or "readonly" or "result") Assert.True(actionRan);
    }
    private sealed class RollbackCancellation(Exception cause, Action seen) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken token) { seen(); Assert.Equal(CancellationToken.None, token); return ValueTask.FromException(cause); }
    }

}
