using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Scheduling;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World.BossTower;

public sealed record RefillOnlineBossTowerTicketCommand(SelectedSessionIdentity Session)
    : ICommand<bool>, IAccountCommand, ISelectedSessionCommand
{ public AccountId AccountId => Session.AccountId; }
internal sealed class RefillOnlineBossTowerTicketCommandHandler(IBossTowerTicketDomainService tickets)
    : ICommandHandler<RefillOnlineBossTowerTicketCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, RefillOnlineBossTowerTicketCommand command, CancellationToken ct)
        => tickets.RefillOnlineTicketAsync(command.Session, ct);
}
internal sealed class BossTowerDailyRefillJob(ISelectedSessionSnapshotQuery online, ISelectedSessionDomainService sessions,
    ICommandBus commands, ILogger<BossTowerDailyRefillJob> logger)
{
    public async ValueTask RunAsync(CancellationToken ct)
    {
        var failures = new List<Exception>();
        foreach (var session in online.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            try { await commands.SendAsync(new RefillOnlineBossTowerTicketCommand(session), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                if (sessions.Find(session.AccountId) != session) continue;
                logger.LogError(error, "Boss tower ticket refill failed character={CharacterId}", session.CharacterId);
                failures.Add(error);
            }
        }
        if (failures.Count > 0) throw new AggregateException("Online Boss tower ticket refill failed.", failures);
    }
}
internal static class BossTowerDailyRefillRegistration
{
    internal static void AddBossTowerDailyRefill(this IServiceCollection services)
    {
        services.AddScoped<BossTowerDailyRefillJob>();
        services.AddCommandHandler<RefillOnlineBossTowerTicketCommand, bool, RefillOnlineBossTowerTicketCommandHandler>(
            CommandExecutionKind.Transactional, accountAccessMode: UserAccessMode.Exclusive);
        services.AddScheduledTaskFactory(sp => new("boss-tower.ticket-refill.online",
            new DailyResetTaskSchedule(sp.GetRequiredService<IDailyResetRuleService>()),
            (context, ct) => context.Services.GetRequiredService<BossTowerDailyRefillJob>().RunAsync(ct)));
    }
}
