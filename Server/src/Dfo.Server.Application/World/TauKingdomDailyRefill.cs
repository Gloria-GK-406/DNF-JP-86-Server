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

public sealed record RefillOnlineTauTicketCommand(SelectedSessionIdentity Session)
    : ICommand<bool>, IAccountCommand, ISelectedSessionCommand
{ public AccountId AccountId => Session.AccountId; }
internal sealed class RefillOnlineTauTicketCommandHandler(ITauKingdomDomainService tau)
    : ICommandHandler<RefillOnlineTauTicketCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, RefillOnlineTauTicketCommand command, CancellationToken ct)
        => tau.RefillOnlineTicketAsync(command.Session, ct);
}
internal sealed class TauKingdomDailyRefillJob(ISelectedSessionSnapshotQuery online, ISelectedSessionDomainService sessions,
    ICommandBus commands, ILogger<TauKingdomDailyRefillJob> logger)
{
    public async ValueTask RunAsync(CancellationToken ct)
    {
        var failures = new List<Exception>();
        foreach (var session in online.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            try { await commands.SendAsync(new RefillOnlineTauTicketCommand(session), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                if (sessions.Find(session.AccountId) != session) continue;
                logger.LogError(error, "Tau ticket refill failed character={CharacterId}", session.CharacterId);
                failures.Add(error);
            }
        }
        if (failures.Count > 0) throw new AggregateException("Online Tau ticket refill failed.", failures);
    }
}
internal static class TauKingdomDailyRefillRegistration
{
    internal static void AddTauKingdomDailyRefill(this IServiceCollection services)
    {
        services.AddScoped<TauKingdomDailyRefillJob>();
        services.AddCommandHandler<RefillOnlineTauTicketCommand, bool, RefillOnlineTauTicketCommandHandler>(
            CommandExecutionKind.Transactional, accountAccessMode: UserAccessMode.Exclusive);
        services.AddScheduledTaskFactory(sp => new("tau-kingdom.ticket-refill.online",
            new DailyResetTaskSchedule(sp.GetRequiredService<IDailyResetRuleService>()),
            (context, ct) => context.Services.GetRequiredService<TauKingdomDailyRefillJob>().RunAsync(ct)));
    }
}
