using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Inventory;

/// <summary>Tops up one mode's ticket for every character in the world when the game day turns.</summary>
/// <remarks>Each character gets its own work unit, so one failed grant neither rolls back nor stops the rest.</remarks>
internal sealed class OnlineTicketRefillJob<TRefill>(ISelectedSessionSnapshotQuery online, ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService users, TRefill tickets, ICommandWorkUnits units, ILogger<OnlineTicketRefillJob<TRefill>> logger)
    : IScheduledGameplayTask where TRefill : IOnlineTicketRefillDomainService
{
    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(CommandExecutionContext execution, ScheduledTaskContext scheduled, CancellationToken ct)
    {
        var failures = new List<Exception>();
        foreach (var session in online.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            if (!sessions.IsInWorld(session)) continue;
            Exception? actionFailure = null;
            var result = await units.WriteResultAsync(execution, new(scheduled.TaskId, [ScheduledSessionRecipient.Capture(session, users)]), async (unit, token) =>
            {
                try { return await tickets.RefillOnlineTicketAsync(session, token).ConfigureAwait(false); }
                catch (Exception error) when (error is not OperationCanceledException
                    && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error)
                    && !Dfo.Server.Application.DomainEvents.DomainEventScopeCleanupException.Contains(error))
                {
                    execution.DemandRequest();
                    unit.RequireRollback(error);
                    actionFailure = error;
                    return false;
                }
            }, ct).ConfigureAwait(false);
            var error = result is CommandWriteResult<bool>.CommitRolledBack rolledBack ? rolledBack.Cause : actionFailure;
            if (error is not null && sessions.IsInWorld(session))
            {
                logger.LogWarning("Online ticket refill {TaskId} failed character={CharacterId}", scheduled.TaskId, session.CharacterId);
                failures.Add(error);
            }
        }
        return ScheduledTaskBusinessResult.Handled(execution, $"Online ticket refill {scheduled.TaskId} failed.", failures);
    }
}

internal static class OnlineTicketRefillExtensions
{
    /// <summary>
    /// Declares the reset job, post-publication selection refill and independent ticket period together.
    /// Selection order is explicit because the scheduler's registration order is a different boundary.
    /// </summary>
    internal static void AddOnlineTicketRefill<TRefill, TRule>(this IServiceCollection services, string taskId,
        int selectionOrder, string selectionWorkUnitName)
        where TRefill : class, IOnlineTicketRefillDomainService
        where TRule : class, IOnlineRefilledTicketRule
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionWorkUnitName);
        ArgumentOutOfRangeException.ThrowIfNegative(selectionOrder);
        services.AddSingleton<OnlineTicketRefillRegistration>(sp =>
            new(taskId, selectionOrder, selectionWorkUnitName, sp.GetRequiredService<TRefill>()));
        services.AddSingleton<IOnlineRefilledTicketRule>(sp => sp.GetRequiredService<TRule>());
        services.AddScoped<OnlineTicketRefillJob<TRefill>>();
        services.AddScheduledTaskFactory(sp => new(taskId,
            new DailyResetTaskSchedule(sp.GetRequiredService<IDailyResetRuleService>()), typeof(OnlineTicketRefillJob<TRefill>)));
    }
}
