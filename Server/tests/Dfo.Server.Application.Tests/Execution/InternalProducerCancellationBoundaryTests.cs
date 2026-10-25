using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Commands;
using Dfo.Server.Application.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Application.Tests.Execution;
public sealed class InternalProducerCancellationBoundaryTests
{
    [Fact]
    public async Task Handler_DI_cancellation_is_not_an_actual_body_occurrence_and_next_request_still_works()
    {
        using var producer = new CancellationTokenSource(); producer.Cancel();
        var cause = new OperationCanceledException("constructor failure", producer.Token);
        var log = new RecordingExecutionLogger<GameplayExecutor>(); var failing = true;
        await using var world = new ProtocolCommandExecutionTests.CommandWorld(configure: services =>
        {
            services.AddSingleton<ILogger<GameplayExecutor>>(log);
            services.AddScoped<ProtocolCommandExecutionTests.ScopedResource>(_ => failing ? throw cause : new());
        });
        Assert.Same(cause, await Record.ExceptionAsync(() => world.Bus.SendInternalAsync(new ProtocolCommandExecutionTests.InternalProbe(), Origin(), producer.Token).AsTask()));
        Assert.True(GameplayJob.EndedAJob(cause)); Assert.Single(log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Empty(world.Settings.Seen); Assert.Empty(world.Settings.Resources);
        failing = false;
        Assert.Equal(1, await world.Bus.SendInternalAsync(new ProtocolCommandExecutionTests.InternalProbe(), Origin()));
    }
    [Fact]
    public async Task Invalid_origin_is_refused_before_enqueue_despite_a_cancelled_producer()
    {
        using var producer = new CancellationTokenSource(); producer.Cancel();
        await using var world = new ProtocolCommandExecutionTests.CommandWorld();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Bus.SendInternalAsync(new ProtocolCommandExecutionTests.InternalProbe(),
            new(GameplayCallSource.Connection, GameplayCallChain.NewRoot(GameplayCallSource.Connection), "bad", InternalCommandCancellation.BodyOnly), producer.Token).AsTask());
        Assert.False(GameplayJob.EndedAJob(error)); Assert.Equal(0, world.Queue.Calls); Assert.Empty(world.Settings.Seen);
    }
    [Fact]
    public async Task Unawaited_cancelled_action_is_drained_before_scope_end_and_cannot_erase_the_unfinished_guard()
    {
        using var producer = new CancellationTokenSource(); producer.Cancel();
        var cause = new OperationCanceledException("held action", producer.Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new RecordingExecutionLogger<GameplayExecutor>();
        await using var world = new ProtocolCommandExecutionTests.CommandWorld(configure: services => services.AddSingleton<ILogger<GameplayExecutor>>(log));
        Task<int>? action = null; DomainTransaction? transaction = null;
        world.Settings.Action = context =>
        {
            action = world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync<int>(context, new("unawaited", []), async (_, _) =>
            {
                transaction = Assert.IsType<DomainTransaction>(world.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
                entered.TrySetResult(); await release.Task; throw cause;
            }, producer.Token).AsTask();
            return ValueTask.FromResult(1);
        };
        var completion = world.Bus.SendInternalAsync(new ProtocolCommandExecutionTests.InternalProbe(), Origin(), producer.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(ExecutionTest.Patience);
            Assert.False(completion.IsCompleted); Assert.False(Assert.Single(world.Settings.Resources).Disposed);
            Assert.False(Assert.Single(world.Settings.Seen).Request!.IsClosed);
        }
        finally { release.TrySetResult(); }
        var failure = await Record.ExceptionAsync(() => completion.WaitAsync(ExecutionTest.Patience));
        Assert.NotNull(failure); Assert.True(GameplayJob.EndedAJob(failure));
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, transaction!.Completion);
        Assert.Same(cause, await Record.ExceptionAsync(() => action!));
        Assert.Single(log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.True(Assert.Single(world.Settings.Resources).Disposed); Assert.True(Assert.Single(world.Settings.Seen).Request!.IsClosed);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_completed_action_cancellation_discarded_or_caught_before_normal_body_return_remains_a_job_failure(bool catchAndReturn)
    {
        using var producer = new CancellationTokenSource(); producer.Cancel();
        var cause = new OperationCanceledException("completed action not propagated by body", producer.Token);
        var log = new RecordingExecutionLogger<GameplayExecutor>();
        await using var world = new ProtocolCommandExecutionTests.CommandWorld(configure: services => services.AddSingleton<ILogger<GameplayExecutor>>(log));
        var accessor = world.Services.GetRequiredService<IDomainTransactionAccessor>();
        var witness = new RollbackWitness();
        DomainTransaction? transaction = null;
        Task<int>? completedAction = null;
        var returnedNormally = false;
        world.Settings.Action = async context =>
        {
            completedAction = world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync<int>(context, new("completed-unpropagated", []), (_, _) =>
            {
                transaction = Assert.IsType<DomainTransaction>(accessor.Current);
                transaction.Enlist(witness, () => witness);
                return ValueTask.FromException<int>(cause);
            }, producer.Token).AsTask();
            // This is the completed-operation boundary, not the held unfinished-action case above.
            Assert.True(completedAction.IsCompleted);
            Assert.False(completedAction.IsCompletedSuccessfully);
            Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, transaction!.Completion);
            Assert.True(transaction.IsSettled);
            Assert.Null(accessor.Current);
            Assert.True(context.Request!.OperationsCompleted);
            if (catchAndReturn)
                Assert.Same(cause, await Record.ExceptionAsync(() => completedAction));
            returnedNormally = true;
            return 1;
        };
        var failure = await Record.ExceptionAsync(() => world.Bus.SendInternalAsync(new ProtocolCommandExecutionTests.InternalProbe(), Origin(), producer.Token).AsTask());
        Assert.True(returnedNormally);
        Assert.Same(cause, failure);
        Assert.Same(cause, await Record.ExceptionAsync(() => completedAction!));
        Assert.Equal(1, witness.Rollbacks);
        Assert.Equal(0, witness.Prepares);
        Assert.Equal(0, witness.Commits);
        Assert.True(Assert.Single(world.Settings.Resources).Disposed);
        Assert.True(Assert.Single(world.Settings.Seen).Request!.IsClosed);
        var errors = log.Entries.Where(entry => entry.Level == LogLevel.Error).ToArray();
        var endedAJob = GameplayJob.EndedAJob(failure!);
        world.Settings.Action = null;
        Assert.Equal(1, await world.Bus.SendInternalAsync(new ProtocolCommandExecutionTests.InternalProbe(), Origin()));
        Assert.Equal(2, world.Settings.Resources.Count);
        Assert.All(world.Settings.Resources, resource => Assert.True(resource.Disposed));
        Assert.All(world.Settings.Seen, context => Assert.True(context.Request!.IsClosed));
        Assert.Equal((1, true), (errors.Length, endedAJob));
        Assert.Same(cause, Assert.Single(errors).Exception);
    }
    private sealed class RollbackWitness : ITransactionParticipant
    {
        internal int Prepares, Commits, Rollbacks;
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token) { Prepares++; return ValueTask.CompletedTask; }
        public ValueTask CommitAsync(CancellationToken token) { Commits++; return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken token)
        { Assert.Equal(CancellationToken.None, token); Rollbacks++; return ValueTask.CompletedTask; }
    }
    private static InternalCommandOrigin Origin() => new(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer), "timer:boundary", InternalCommandCancellation.BodyOnly);
}
