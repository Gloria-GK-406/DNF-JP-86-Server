using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Events;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Application.Events;

internal sealed class JoustMaintenanceDomainService(IEventService events, ILogger<JoustMaintenanceDomainService> logger) : IJoustMaintenanceDomainService
{
    private JoustState? observed;
    private readonly HashSet<string> openedRounds = [];
    public ValueTask PublishCompletedAsync(JoustState state)
    { GameplayWriteAssertion.Writing(nameof(JoustMaintenanceDomainService) + ".PublishCompletedAsync"); return PublishAsync(state, JoustLifecycleTransition.RoundEnded); }
    public async ValueTask ObserveAsync(JoustState state, bool suppressNotifications)
    {
        GameplayWriteAssertion.Writing(nameof(JoustMaintenanceDomainService) + ".ObserveAsync");
        var previous = observed;
        observed = state;
        if (suppressNotifications) { if (state.RoundId is {} restored) openedRounds.Add(restored); return; }
        if (previous?.Active == state.Active && previous?.RoundId == state.RoundId && previous?.Phase == state.Phase) return;
        if (state.Active && state.Phase == JoustPhase.Betting && state.RoundId is {} round && openedRounds.Add(round))
            await PublishAsync(state, JoustLifecycleTransition.RoundStarted).ConfigureAwait(false);
        else if (state.Phase != JoustPhase.Result)
            await PublishAsync(state, JoustLifecycleTransition.PhaseChanged).ConfigureAwait(false);
    }
    private async ValueTask PublishAsync(JoustState state, JoustLifecycleTransition kind)
    {
        try { await events.DirectAsync(new JoustLifecycleChanged(state, kind), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException && !DomainEventScopeCleanupException.Contains(error) && !TransactionOutcomeUncertainException.Contains(error)) { GameplayJob.DemandHealthy(); logger.LogWarning(error, "Committed joust notification {Kind} for round {Round} failed; no replay.", kind, state.RoundId); }
    }
}
internal sealed record MaintainJoustCommand(bool SuppressNotifications) : ICommand<bool>;
internal sealed class MaintainJoustCommandHandler(IJoustDomainService joust, IJoustProjectionDomainService projection,
    IJoustMaintenanceDomainService maintenance, ICommandWorkUnits units, TimeProvider clock) : ICommandHandler<MaintainJoustCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, MaintainJoustCommand command, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var advanced = await joust.NeedsAdvanceAsync(now, token).ConfigureAwait(false)
            ? await units.WriteAsync(context, new("joust.lifecycle", []), (_, ct) => joust.AdvanceLifecycleAsync(now, ct), token).ConfigureAwait(false)
            : new JoustLifecycleResult(0);
        if (!command.SuppressNotifications)
            foreach (var completed in advanced.CompletedRounds)
            { await maintenance.PublishCompletedAsync(completed).ConfigureAwait(false); context.Request!.DemandCurrent(); }
        var state = await projection.ReadAsync(new(0), now, token).ConfigureAwait(false);
        await maintenance.ObserveAsync(state, command.SuppressNotifications).ConfigureAwait(false);
        context.Request!.DemandCurrent();
        return true;
    }
}
internal sealed class JoustMaintenanceService(ICommandBus commands, TimeProvider clock, ILogger<JoustMaintenanceService> logger)
    : TypedGameplayTimerSource(commands, logger)
{
    protected override string SourceName => "timer:JoustMaintenanceService";
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await TickAsync(cancellationToken, true).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }
    internal ValueTask TickAsync(CancellationToken token, bool suppressNotifications = false) =>
        new(RunAsync(token, suppressNotifications ? GameplayCallSource.StartupRecovery : GameplayCallSource.Timer));
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token) =>
        _ = await SubmitAsync(new MaintainJoustCommand(root.Source == GameplayCallSource.StartupRecovery), root,
            root.CommandType!, token).ConfigureAwait(false);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try { await HostedAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Joust advancement or projection failed; next tick re-evaluates persisted state."); }
        }
    }
}
