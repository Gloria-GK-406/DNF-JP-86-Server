using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Scheduling;

namespace Dfo.Server.Application.Events;

public sealed record InitializeManagedEventsCommand : ICommand<bool>;

internal sealed class InitializeManagedEventsCommandHandler(IManagedEventLifecycleDomainService lifecycle,
    ICommandWorkUnits units, TimeProvider clock) : ICommandHandler<InitializeManagedEventsCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, InitializeManagedEventsCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        foreach (var id in lifecycle.EventIds)
            await units.WriteAsync(context, new("activity.startup-reconciliation", []),
                (_, token) => lifecycle.ReconcileAsync(id, now, token, notifyClients: false), ct).ConfigureAwait(false);
        return true;
    }
}

internal sealed class ManagedEventLifecycleTask(IManagedEventLifecycleDomainService lifecycle,
    ICommandWorkUnits units, TimeProvider clock) : IScheduledGameplayTask
{
    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(CommandExecutionContext execution, ScheduledTaskContext scheduled, CancellationToken ct)
    {
        var failures = new List<Exception>();
        var now = clock.GetUtcNow();
        foreach (var id in lifecycle.EventIds)
        {
            Exception? actionFailure = null;
            var result = await units.WriteResultAsync(execution, new("activity.scheduled-reconciliation", []), async (unit, token) =>
            {
                try { return await lifecycle.ReconcileAsync(id, now, token).ConfigureAwait(false); }
                catch (Exception error) when (error is not OperationCanceledException
                    && !TransactionOutcomeUncertainException.Contains(error) && !DomainEventScopeCleanupException.Contains(error))
                {
                    execution.DemandRequest();
                    unit.RequireRollback(error);
                    actionFailure = error;
                    return false;
                }
            }, ct).ConfigureAwait(false);
            var failure = result is CommandWriteResult<bool>.CommitRolledBack rolledBack ? rolledBack.Cause : actionFailure;
            if (failure is not null) failures.Add(failure);
        }
        return ScheduledTaskBusinessResult.Handled(execution, "Activity reconciliation failed for some activities.", failures);
    }
}
