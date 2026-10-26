using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Scheduling;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

internal sealed class SouthernDaleDailyRefillJob(ISelectedSessionSnapshotQuery online, ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ISouthernDaleDomainService southern, ICommandWorkUnits units, Microsoft.Extensions.Logging.ILogger<SouthernDaleDailyRefillJob> logger) : IScheduledGameplayTask
{
    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(CommandExecutionContext execution, ScheduledTaskContext scheduled, CancellationToken ct)
    {
        var failures = new List<Exception>();
        foreach (var session in online.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            if (!sessions.IsInWorld(session)) continue;
            Exception? actionFailure = null;
            var result = await units.WriteResultAsync(execution, new("southern-dale.ticket-refill.online", [ScheduledSessionRecipient.Capture(session, users)]), async (unit, token) =>
            {
                try { return await southern.RefillOnlineTicketAsync(session, token).ConfigureAwait(false); }
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
                logger.LogError(error, "Southern Dale ticket refill failed character={CharacterId}", session.CharacterId);
                failures.Add(error);
            }
        }
        return ScheduledTaskBusinessResult.Handled(execution, "Online Southern Dale ticket refill failed.", failures);
    }
}

internal static class SouthernDaleDailyRefillRegistration
{
    internal static void AddSouthernDaleDailyRefill(this IServiceCollection services)
    {
        services.AddScoped<SouthernDaleDailyRefillJob>();
        services.AddScheduledTaskFactory(sp => new("southern-dale.ticket-refill.online", new DailyResetTaskSchedule(sp.GetRequiredService<IDailyResetRuleService>()), typeof(SouthernDaleDailyRefillJob)));

    }
}
