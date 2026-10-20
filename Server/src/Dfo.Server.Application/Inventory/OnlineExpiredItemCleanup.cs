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
    ISelectedSessionRuntimeDirectory published, IExpiredCharacterItemCleanupDomainService cleanup,
    IEventService events, ICreatureRuntimeDomainService creatures, IAuthenticatedGamePresenceQuery online,
    IQuestHelperPuppetDomainService? puppets = null) : IOnlineExpiredItemCleanupDomainService
{
    public async ValueTask<bool> CleanupAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        if (sessions.Find(session.AccountId) != session || sessions.FindByConnection(session.ConnectionId) != session
            || !published.IsPublishedExact(session) || !online.IsActiveExact(session)) return false;
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

public sealed record CleanupOnlineExpiredItemsCommand(SelectedSessionIdentity Session)
    : ICommand<bool>, IAccountCommand, ISelectedSessionCommand
{
    public AccountId AccountId => Session.AccountId;
}

internal sealed class CleanupOnlineExpiredItemsCommandHandler(IOnlineExpiredItemCleanupDomainService cleanup)
    : ICommandHandler<CleanupOnlineExpiredItemsCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context,
        CleanupOnlineExpiredItemsCommand command, CancellationToken ct) => cleanup.CleanupAsync(command.Session, ct);
}

internal sealed class OnlineExpiredItemCleanupJob(ISelectedSessionSnapshotQuery online, ICommandBus commands,
    ISelectedSessionDomainService sessions, ILogger<OnlineExpiredItemCleanupJob> logger)
{
    public async ValueTask RunAsync(CancellationToken ct)
    {
        var failures = new List<Exception>();
        foreach (var session in online.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            try { await commands.SendAsync(new CleanupOnlineExpiredItemsCommand(session), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                // A selected command deliberately rejects a generation displaced while queued.
                if (sessions.Find(session.AccountId) != session) continue;
                logger.LogError(error, "Online expired-item cleanup failed account={AccountId} character={CharacterId}",
                    session.AccountId, session.CharacterId);
                failures.Add(error);
            }
        }
        if (failures.Count > 0) throw new AggregateException("Online expired-item cleanup had failed characters.", failures);
    }
}

internal sealed class DailyResetTaskSchedule(IDailyResetRuleService daily) : IScheduledTaskSchedule
{
    public DateTimeOffset LastOccurrence(DateTimeOffset at) => daily.PeriodStart(at);
    public DateTimeOffset NextOccurrence(DateTimeOffset after) => daily.PeriodStart(after).AddDays(1);
}

internal static class OnlineExpiredItemCleanupRegistration
{
    internal static void AddOnlineExpiredItemCleanup(this IServiceCollection services)
    {
        services.AddSingleton<IOnlineExpiredItemCleanupDomainService, OnlineExpiredItemCleanupDomainService>();
        services.AddScoped<OnlineExpiredItemCleanupJob>();
        services.AddCommandHandler<CleanupOnlineExpiredItemsCommand, bool, CleanupOnlineExpiredItemsCommandHandler>(
            CommandExecutionKind.Transactional, accountAccessMode: UserAccessMode.Exclusive);
        services.AddDomainEvent<OnlineExpiredItemsRemoved>("inventory.expired-items-removed", 1);
        services.AddScheduledTaskFactory(sp => new("inventory.expiration.online",
            new DailyResetTaskSchedule(sp.GetRequiredService<IDailyResetRuleService>()),
            (context, ct) => context.Services.GetRequiredService<OnlineExpiredItemCleanupJob>().RunAsync(ct)));
    }
}
