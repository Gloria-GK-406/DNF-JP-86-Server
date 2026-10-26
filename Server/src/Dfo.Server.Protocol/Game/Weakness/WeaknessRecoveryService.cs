using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Weakness;

public sealed class WeaknessProjectionDelivery(
    IWeaknessDomainService weakness, ISelectedSessionDomainService sessions, IGameMessageSender sender)
{
    public async ValueTask SendCurrentAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        if (!sessions.IsInWorld(session)) return;
        await sender.SendNotiAsync(session.ConnectionId,
            new WeaknessProjectionNotification(weakness.For(session.CharacterId).Value),
            cancellationToken).ConfigureAwait(false);
    }
}

public sealed record RecoverWeaknessTickCommand : ICommand<bool>;
public sealed class RecoverWeaknessTickCommandHandler(IWeaknessDomainService weakness, ISelectedSessionDomainService sessions,
    ICommandWorkUnits units, IGameCommandOutbox outbox, ILogger<RecoverWeaknessTickCommandHandler> logger)
    : ICommandHandler<RecoverWeaknessTickCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, RecoverWeaknessTickCommand command, CancellationToken token)
    {
        var changed = await units.WriteAsync(context, new("weakness.recovery", []), async (_, ct) =>
        {
            var recovered = weakness.AdvanceRecovery();
            var retried = await weakness.RetryPendingWeakness(ct).ConfigureAwait(false);
            return recovered.Concat(retried).Distinct().ToArray();
        }, token).ConfigureAwait(false);
        foreach (var id in changed)
        {
            if (!sessions.TryGetInWorld(id, out var session)) continue;
            WeaknessProjectionNotification message;
            try { message = new(weakness.For(session.CharacterId).Value); }
            catch (Exception error) when (error is not OperationCanceledException && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error))
            { logger.LogWarning(error, "Weakness projection failed character={CharacterId}", id); continue; }
            await outbox.RegisterNotiAsync(session.ConnectionId, message, token).ConfigureAwait(false);
        }
        return true;
    }
}
public sealed class WeaknessRecoveryService(ICommandBus commands, TimeSpan recoveryInterval, TimeProvider clock,
    ILogger<WeaknessRecoveryService> logger) : TypedGameplayTimerSource(commands, logger)
{
    protected override string SourceName => "timer:WeaknessRecoveryService";
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token) =>
        _ = await SubmitAsync(new RecoverWeaknessTickCommand(), root, SourceName, token).ConfigureAwait(false);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(recoveryInterval, clock);
        try { while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) await HostedAsync(stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
