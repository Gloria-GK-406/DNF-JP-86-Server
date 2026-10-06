using Dfo.Server.Data.Events;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Application.Events;
internal sealed class JoustMaintenanceService(ICommandBus bus,IJoustDomainService joust,IJoustProjectionDomainService projection,
    ISelectedSessionSnapshotQuery selected,INotificationBus notices,TimeProvider clock,ILogger<JoustMaintenanceService> logger) : BackgroundService
{
    private (bool Active,string? Round,JoustPhase Phase)? delivered;
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Recover before the host becomes ready, so first entry sees the current absolute-time phase.
        await TickAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }
    internal async ValueTask TickAsync(CancellationToken ct)
    {
        var now=clock.GetUtcNow();
        var settled=await joust.NeedsAdvanceAsync(now,ct) && await bus.SendAsync(new AdvanceJoustCommand(now),ct);
        var state=await projection.ReadAsync(new(0),now,ct);
        var signature=(state.Active,state.RoundId,state.Phase);
        var sessions=selected.Snapshot();
        if(settled)
            foreach(var session in sessions)await notices.PublishAsync(new NotificationAudience.TheseSessions([session]),new MailboxChanged(session.CharacterId),ct);
        if(delivered!=signature)
        {
            await notices.PublishAsync(new NotificationAudience.TheseSessions(sessions),new JoustChanged(now),ct);
            delivered=signature;
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1),clock);
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try{await TickAsync(stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"Joust advancement or committed projection failed; next tick retries.");}
        }
    }
}
