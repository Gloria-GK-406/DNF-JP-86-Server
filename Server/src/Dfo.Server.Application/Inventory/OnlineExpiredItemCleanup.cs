using System.Collections.Immutable;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Scheduling;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Inventory;

internal sealed class OnlineExpiredItemCleanupDomainService(ISelectedSessionDomainService sessions,
    IExpiredCharacterItemCleanupDomainService cleanup,
    IEventService events, ICreatureRuntimeDomainService creatures,
    IQuestHelperPuppetDomainService? puppets = null) : IOnlineExpiredItemCleanupDomainService
{
    public async ValueTask<bool> CleanupAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        if (!sessions.IsInWorld(session)) return false;
        var puppetChange = puppets is null ? null : await puppets.ReconcileAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var result = await cleanup.CleanupAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        if (!result.Owned) throw new InvalidOperationException("Online character ownership changed during expiry cleanup.");
        var containers = result.ChangedContainers.Concat(puppetChange?.ChangedContainers ?? []).Distinct().Order().ToImmutableArray();
        if (containers.Length == 0 && puppetChange?.MailboxChanged != true) return true;
        if (containers.Contains(InventoryContainerType.Equipment)
            || containers.Contains(InventoryContainerType.Pet))
            await creatures.AdvanceAsync(session, ct).ConfigureAwait(false);
        await events.EnqueueAsync(new OnlineExpiredItemsRemoved(session.AccountId, session.CharacterId, session.ConnectionId.Value,
            session.ConnectionGeneration, containers) { MailboxChanged = puppetChange?.MailboxChanged == true }, ct).ConfigureAwait(false);
        return true;
    }
}

internal sealed class DailyResetTaskSchedule(IDailyResetRuleService daily) : IScheduledTaskSchedule
{
    public DateTimeOffset LastOccurrence(DateTimeOffset at) => daily.PeriodStart(at);
    public DateTimeOffset NextOccurrence(DateTimeOffset after) => daily.PeriodStart(after).AddDays(1);
}

internal sealed class OnlineExpiredItemCleanupJob(ISelectedSessionSnapshotQuery online, ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    IOnlineExpiredItemCleanupDomainService cleanup, ICommandWorkUnits units, Microsoft.Extensions.Logging.ILogger<OnlineExpiredItemCleanupJob> logger) : IScheduledGameplayTask
{
    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(CommandExecutionContext execution, ScheduledTaskContext scheduled, CancellationToken ct)
    {
        var failures = new List<Exception>();
        foreach (var session in online.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            if (!sessions.IsInWorld(session)) continue;
            Exception? actionFailure = null;
            var result = await units.WriteResultAsync(execution, new("inventory.expiration.online", [ScheduledSessionRecipient.Capture(session, users)]), async (unit, token) =>
            {
                try { return await cleanup.CleanupAsync(session, token).ConfigureAwait(false); }
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
                logger.LogWarning( "Online expired-item cleanup failed account={AccountId} character={CharacterId}", session.AccountId, session.CharacterId);
                failures.Add(error);
            }
        }
        return ScheduledTaskBusinessResult.Handled(execution, "Online expired-item cleanup had failed characters.", failures);
    }
}

internal static class OnlineExpiredItemCleanupRegistration
{
    internal static void AddOnlineExpiredItemCleanup(this IServiceCollection services)
    {
        services.AddSingleton<IOnlineExpiredItemCleanupDomainService, OnlineExpiredItemCleanupDomainService>();
        services.AddScoped<OnlineExpiredItemCleanupJob>();
        services.AddDomainEvent<OnlineExpiredItemsRemoved>("inventory.expired-items-removed", 1);
        services.AddScheduledTaskFactory(sp => new("inventory.expiration.online", new DailyResetTaskSchedule(sp.GetRequiredService<IDailyResetRuleService>()), typeof(OnlineExpiredItemCleanupJob)));

    }
}
