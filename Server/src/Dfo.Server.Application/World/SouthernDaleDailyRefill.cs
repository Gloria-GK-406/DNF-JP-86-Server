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

public sealed record RefillOnlineSouthernTicketCommand(SelectedSessionIdentity Session)
    : ICommand<bool>, IAccountCommand, ISelectedSessionCommand
{ public AccountId AccountId => Session.AccountId; }
internal sealed class RefillOnlineSouthernTicketCommandHandler(ISouthernDaleDomainService southern)
    : ICommandHandler<RefillOnlineSouthernTicketCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, RefillOnlineSouthernTicketCommand command, CancellationToken ct)
        => southern.RefillOnlineTicketAsync(command.Session, ct);
}
internal sealed class SouthernDaleDailyRefillJob(ISelectedSessionSnapshotQuery online, ISelectedSessionDomainService sessions,
    ICommandBus commands, ILogger<SouthernDaleDailyRefillJob> logger)
{
    public async ValueTask RunAsync(CancellationToken ct)
    {
        var failures = new List<Exception>();
        foreach (var session in online.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            try { await commands.SendAsync(new RefillOnlineSouthernTicketCommand(session), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                if (sessions.Find(session.AccountId) != session) continue;
                logger.LogError(error, "Southern Dale ticket refill failed character={CharacterId}", session.CharacterId);
                failures.Add(error);
            }
        }
        if (failures.Count > 0) throw new AggregateException("Online Southern Dale ticket refill failed.", failures);
    }
}
internal static class SouthernDaleDailyRefillRegistration
{
    internal static void AddSouthernDaleDailyRefill(this IServiceCollection services)
    {
        services.AddScoped<SouthernDaleDailyRefillJob>();
        services.AddCommandHandler<RefillOnlineSouthernTicketCommand, bool, RefillOnlineSouthernTicketCommandHandler>(
            CommandExecutionKind.Transactional, accountAccessMode: UserAccessMode.Exclusive);
        services.AddScheduledTaskFactory(sp => new("southern-dale.ticket-refill.online",
            new DailyResetTaskSchedule(sp.GetRequiredService<IDailyResetRuleService>()),
            (context, ct) => context.Services.GetRequiredService<SouthernDaleDailyRefillJob>().RunAsync(ct)));
    }
}
