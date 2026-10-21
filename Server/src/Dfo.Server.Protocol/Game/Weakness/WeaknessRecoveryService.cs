using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Weakness;

public sealed record AdvanceWeaknessRecoveryCommand : ICommand<IReadOnlyList<CharacterId>>;

public sealed class AdvanceWeaknessRecoveryCommandHandler(IWeaknessDomainService weakness)
    : ICommandHandler<AdvanceWeaknessRecoveryCommand, IReadOnlyList<CharacterId>>
{
    public async ValueTask<IReadOnlyList<CharacterId>> HandleAsync(CommandExecutionContext context,
        AdvanceWeaknessRecoveryCommand command, CancellationToken cancellationToken)
    {
        var recovered = weakness.AdvanceRecovery();
        var retried = await weakness.RetryPendingWeakness(cancellationToken).ConfigureAwait(false);
        return recovered.Concat(retried).Distinct().ToArray();
    }
}

public sealed class WeaknessProjectionDelivery(
    IWeaknessDomainService weakness, ISelectedSessionDomainService sessions, IGameMessageSender sender)
{
    private readonly SemaphoreSlim deliveryGate = new(1, 1);

    public async ValueTask SendCurrentAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        await deliveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!sessions.IsInWorld(session)) return;
            await sender.SendNotiAsync(session.ConnectionId,
                new WeaknessProjectionNotification(weakness.For(session.CharacterId).Value),
                cancellationToken).ConfigureAwait(false);
        }
        finally { deliveryGate.Release(); }
    }
}

public sealed class WeaknessRecoveryService(
    IWeaknessDomainService weakness, ICommandBus commands,
    ISelectedSessionDomainService sessions, SelectedSessionTransitionGate transitions,
    WeaknessProjectionDelivery delivery, TimeProvider clock,
    ILogger<WeaknessRecoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(weakness.RecoveryInterval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var changed = await commands.SendAsync(new AdvanceWeaknessRecoveryCommand(), stoppingToken)
                    .ConfigureAwait(false);
                foreach (var id in changed)
                {
                    try
                    {
                        if (!sessions.TryGetInWorld(id, out var session)) continue;
                        await using var transition = await transitions.AcquireAsync(session.AccountId,
                            UserAccessMode.Exclusive, stoppingToken).ConfigureAwait(false);
                        await delivery.SendCurrentAsync(session, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception exception) { logger.LogWarning(exception, "Weakness projection failed character={CharacterId}", id); }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
