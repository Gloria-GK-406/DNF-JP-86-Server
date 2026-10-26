using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Scheduling;

public interface IScheduledGameplayTask
{
    ValueTask<ScheduledTaskBusinessResult> RunAsync(CommandExecutionContext execution, ScheduledTaskContext scheduled, CancellationToken token);
}
internal sealed class ScheduledTaskRunState
{
    internal bool Started { get; set; }
    internal DateTimeOffset Requested { get; set; }
    internal DateTimeOffset? ActualScheduledAt { get; private set; }
    internal void Reset(DateTimeOffset requested)
    { lock (this) { Started = false; Requested = requested; ActualScheduledAt = null; } }
    internal ScheduledTaskContext BeginAttempt(string id, int attempt, TimeProvider clock)
    {
        lock (this) { Started = true; ActualScheduledAt = Requested; return new(id, Requested, clock.GetUtcNow(), attempt); }
    }
}
public sealed class ScheduledTaskBusinessResult
{
    private ScheduledTaskBusinessResult(AggregateException? error) => Error = error;
    public static ScheduledTaskBusinessResult Success { get; } = new(null);
    internal AggregateException? Error { get; }
    internal static ScheduledTaskBusinessResult Handled(CommandExecutionContext context, string message, IReadOnlyList<Exception> failures)
    {
        context.DemandRequest();
        return failures.Count == 0 ? Success : new(new AggregateException(message, failures));
    }
}
internal sealed record ScheduledGameplayTaskCommand(string TaskId, ScheduledTaskRunState State, int Attempt) : ICommand<ScheduledGameplayTaskResult>;
internal sealed class ScheduledGameplayTaskResult
{
    private readonly ScheduledGameplayTaskCommand owner;
    private readonly CommandRequestContext request;
    private readonly Guid job;
    private readonly AggregateException? failure;
    internal ScheduledGameplayTaskResult(ScheduledGameplayTaskCommand owner, CommandExecutionContext context,
        ScheduledTaskContext scheduled, ScheduledTaskBusinessResult result)
    {
        this.owner = owner; request = context.DemandRequest(); job = GameplayJob.Current!.Value.Id;
        ScheduledAt = scheduled.ScheduledAt; failure = result.Error;
    }
    internal DateTimeOffset ScheduledAt { get; }
    internal void ThrowCompletedBusinessFailure(ScheduledGameplayTaskCommand command)
    {
        if (!ReferenceEquals(owner, command) || GameplayJob.Current?.Id != job || !request.IsClosed
            || request.Failure is not null || !request.OperationsCompleted
            || request is not CommandRequestContext.TrustedInternal { Origin.Kind: GameplayCallSource.Timer, Origin.SourceConnection: null })
            throw new InvalidOperationException("The scheduled result does not belong to this fully completed command.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
    }
}
public interface ICommandScheduledTaskDispatch
{
    internal ValueTask<ScheduledGameplayTaskResult> RunAsync(CommandExecutionContext execution, ScheduledGameplayTaskCommand command, CancellationToken token);
}
internal sealed class CommandScheduledTaskDispatch(IServiceProvider services,
    IEnumerable<ScheduledTaskRegistration> registrations, TimeProvider clock) : ICommandScheduledTaskDispatch
{
    private readonly IReadOnlyDictionary<string, ScheduledTaskRegistration> registrations = registrations.ToDictionary(x => x.Id, StringComparer.Ordinal);
    public async ValueTask<ScheduledGameplayTaskResult> RunAsync(CommandExecutionContext execution, ScheduledGameplayTaskCommand command, CancellationToken token)
    {
        var request = execution.DemandRequest();
        if (execution.CommandType != typeof(ScheduledGameplayTaskCommand)
            || request is not CommandRequestContext.TrustedInternal { Origin.Kind: GameplayCallSource.Timer, Origin.SourceConnection: null })
            throw new InvalidOperationException("Scheduled dispatch requires its exact internal timer command.");
        var scheduled = command.State.BeginAttempt(command.TaskId, command.Attempt, clock);
        token.ThrowIfCancellationRequested();
        var registration = registrations[command.TaskId];
        var handler = (IScheduledGameplayTask)services.GetRequiredService(registration.HandlerType);
        var result = await handler.RunAsync(execution, scheduled, token).ConfigureAwait(false);
        return new(command, execution, scheduled, result);
    }
}
internal sealed class ScheduledGameplayTaskCommandHandler(ICommandScheduledTaskDispatch tasks)
    : ICommandHandler<ScheduledGameplayTaskCommand, ScheduledGameplayTaskResult>
{
    public ValueTask<ScheduledGameplayTaskResult> HandleAsync(CommandExecutionContext context, ScheduledGameplayTaskCommand command, CancellationToken token) =>
        tasks.RunAsync(context, command, token);
}
