using Dfo.Server.Data.Events;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Application.Events;
internal sealed class JoustMaintenanceService(ICommandBus bus,IJoustDomainService joust,IJoustProjectionDomainService projection,
    IEventService events,TimeProvider clock,IGameplayQueue queue,ILogger<JoustMaintenanceService> logger) : BackgroundService
{
    private JoustState? observed;
    private readonly HashSet<string> openedRounds=[];
    private readonly SemaphoreSlim ticks=new(1,1);
    private readonly CoalescingGameplaySource periodic=new(queue);
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Recover before the host becomes ready, so first entry sees the current absolute-time phase.
        await TickAsync(cancellationToken,suppressNotifications:true);
        await base.StartAsync(cancellationToken);
    }
    // One tick is one CMD on the executor; startup recovery is the same body as a startup CMD.
    internal ValueTask TickAsync(CancellationToken ct,bool suppressNotifications=false)=>new(suppressNotifications
        ? GameplayTimerWork.RunAsync(queue,"startup:JoustMaintenanceService",t=>TickCoreAsync(t,true),ct,GameplayCallSource.StartupRecovery)
        : GameplayTimerWork.RunAsync(queue,"timer:JoustMaintenanceService",t=>TickCoreAsync(t,false),ct));
    private async ValueTask TickCoreAsync(CancellationToken ct,bool suppressNotifications)
    {
        await ticks.WaitAsync(ct);
        try{await AdvanceAndPublishAsync(ct,suppressNotifications);}
        finally{ticks.Release();}
    }
    private async ValueTask AdvanceAndPublishAsync(CancellationToken ct,bool suppressNotifications)
    {
        var now=clock.GetUtcNow();
        var advanced=await joust.NeedsAdvanceAsync(now,ct)
            ? await bus.SendAsync(new AdvanceJoustLifecycleCommand(now),ct) : new JoustLifecycleResult(0);
        // Settlement is already committed and will not be returned by a later tick. Publish
        // its captured outcome before reading the next round's projection.
        if(!suppressNotifications)
            foreach(var completed in advanced.CompletedRounds)
                await PublishTransitionAsync(completed,JoustLifecycleTransition.RoundEnded);
        var state=await projection.ReadAsync(new(0),now,ct);
        var previous=observed;
        // Consume the transition before publication. A failed recipient must not replay it
        // for recipients who already heard it. Startup recovery establishes a silent baseline.
        observed=state;
        if(suppressNotifications)
        {
            if(state.RoundId is {} restored)openedRounds.Add(restored);
            return;
        }
        if(previous?.Active==state.Active && previous?.RoundId==state.RoundId && previous?.Phase==state.Phase)return;
        if(state.Active && state.Phase==JoustPhase.Betting && state.RoundId is {} round && openedRounds.Add(round))
        {
            await PublishTransitionAsync(state,JoustLifecycleTransition.RoundStarted);
        }
        else if(state.Phase!=JoustPhase.Result)
            await PublishTransitionAsync(state,JoustLifecycleTransition.PhaseChanged);
    }
    private async ValueTask PublishTransitionAsync(JoustState state,JoustLifecycleTransition kind)
    {
        try
        {
            await events.DirectAsync(new JoustLifecycleChanged(state,kind),CancellationToken.None);
        }
        catch(Exception error)
        {
            // The transition is consumed before delivery; failure must not replay an announcement.
            logger.LogWarning(error,"Committed joust notification {Kind} for round {Round} failed; no replay.",kind,state.RoundId);
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1),clock);
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try{await GameplayTimerWork.TickAsync(periodic,"timer:JoustMaintenanceService",t=>TickCoreAsync(t,false),stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"Joust advancement or projection failed; next tick re-evaluates persisted state.");}
        }
    }
}
