using Dfo.Server.Data.Events;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Application.Events;
internal sealed class JoustMaintenanceService(ICommandBus bus,IJoustDomainService joust,IJoustProjectionDomainService projection,
    IDomainEventBus events,TimeProvider clock,ILogger<JoustMaintenanceService> logger) : BackgroundService
{
    private JoustState? observed;
    private readonly HashSet<string> openedRounds=[];
    private readonly SemaphoreSlim ticks=new(1,1);
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Recover before the host becomes ready, so first entry sees the current absolute-time phase.
        await TickAsync(cancellationToken,suppressEvents:true);
        await base.StartAsync(cancellationToken);
    }
    internal async ValueTask TickAsync(CancellationToken ct,bool suppressEvents=false)
    {
        await ticks.WaitAsync(ct);
        try{await AdvanceAndPublishAsync(ct,suppressEvents);}
        finally{ticks.Release();}
    }
    private async ValueTask AdvanceAndPublishAsync(CancellationToken ct,bool suppressEvents)
    {
        var now=clock.GetUtcNow();
        var advanced=await joust.NeedsAdvanceAsync(now,ct)
            ? await bus.SendAsync(new AdvanceJoustLifecycleCommand(now),ct) : new JoustLifecycleResult(0);
        // Settlement is already committed and will not be returned by a later tick. Publish
        // its captured outcome before reading the next round's projection.
        if(!suppressEvents)
            foreach(var completed in advanced.CompletedRounds)
                await events.PublishAsync(new JoustLifecycleEvent(completed,JoustLifecycleEventKind.RoundEnded),CancellationToken.None);
        var state=await projection.ReadAsync(new(0),now,ct);
        var previous=observed;
        // Consume the transition before publication. A failed recipient must not replay it
        // for recipients who already heard it. Startup recovery establishes a silent baseline.
        observed=state;
        if(suppressEvents)
        {
            if(state.RoundId is {} restored)openedRounds.Add(restored);
            return;
        }
        if(previous?.Active==state.Active && previous?.RoundId==state.RoundId && previous?.Phase==state.Phase)return;
        if(state.Active && state.Phase==JoustPhase.Betting && state.RoundId is {} round && openedRounds.Add(round))
        {
            await events.PublishAsync(new JoustLifecycleEvent(state,JoustLifecycleEventKind.RoundStarted),CancellationToken.None);
        }
        else if(state.Phase!=JoustPhase.Result)
            await events.PublishAsync(new JoustLifecycleEvent(state,JoustLifecycleEventKind.PhaseChanged),CancellationToken.None);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1),clock);
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try{await TickAsync(stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"Joust advancement or projection failed; next tick re-evaluates persisted state.");}
        }
    }
}
