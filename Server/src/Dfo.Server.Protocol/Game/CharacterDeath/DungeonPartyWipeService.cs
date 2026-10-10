using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CharacterDeath;

public sealed record EndPartyWipeCommand(DungeonRunId RunId, DateTimeOffset Now) : ICommand<EndPartyWipeResult>;
public sealed record EndPartyWipeResult(DungeonRunState? Ended);
public sealed class EndPartyWipeCommandHandler(IDungeonRunDomainService runs)
    : ICommandHandler<EndPartyWipeCommand, EndPartyWipeResult>
{
    public async ValueTask<EndPartyWipeResult> HandleAsync(CommandExecutionContext context,
        EndPartyWipeCommand command, CancellationToken cancellationToken) =>
        new(await runs.TryEndPartyWipeAsync(command.RunId, command.Now).ConfigureAwait(false));
}

public sealed record EndClearReturnCommand(DungeonRunId RunId, DateTimeOffset Now) : ICommand<EndPartyWipeResult>;
public sealed class EndClearReturnCommandHandler(IDungeonRunDomainService runs)
    : ICommandHandler<EndClearReturnCommand, EndPartyWipeResult>
{
    public async ValueTask<EndPartyWipeResult> HandleAsync(CommandExecutionContext context,
        EndClearReturnCommand command, CancellationToken cancellationToken) =>
        new(await runs.TryEndClearReturnAsync(command.RunId, command.Now).ConfigureAwait(false));
}

/// <summary>Observes shared life state, then returns only the exact generations admitted to an expired run.</summary>
public sealed class DungeonPartyWipeService(
    IDungeonRunDomainService runs,
    ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterPositionDomainService positions,
    DungeonRunDeliveryRegistry registry,
    DungeonProjectionGate projectionGate,
    SelectedSessionTransitionGate transitionGate,
    DungeonCardPresentationCoordinator cards,
    TownAreaProjectionDelivery towns,
    ICommandBus commands,
    TimeProvider clock,
    ILogger<DungeonPartyWipeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                foreach (var runId in runs.FindExpiredClearReturns(clock.GetUtcNow()))
                {
                    try { await ReturnAsync(runId, stoppingToken, cleared: true).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception exception) { logger.LogError(exception, "Clear timeout return failed run={RunId}", runId); }
                }
                foreach (var runId in runs.FindExpiredPartyWipes(clock.GetUtcNow()))
                {
                    try { await ReturnAsync(runId, stoppingToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception exception) { logger.LogError(exception, "Party wipe return failed run={RunId}", runId); }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal async ValueTask ReturnAsync(DungeonRunId runId, CancellationToken cancellationToken, bool cleared = false)
    {
        await using var projection = await projectionGate.AcquireAsync(
            runId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
        var frozen = registry.Find(runId);
        var leases = new List<IAsyncDisposable>();
        try
        {
            // The same ordering as entry: instance, then accounts in stable order.
            foreach (var account in frozen.Select(x => x.AccountId).Distinct().OrderBy(x => x.Value))
                leases.Add(await transitionGate.AcquireAsync(account, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false));
            var entries = new List<TownAreaEntry>();
            foreach (var session in frozen)
            {
                if (sessions.FindByCharacter(session.CharacterId) != session
                    || sessions.FindByConnection(session.ConnectionId) != session
                    || runs.Find(session.CharacterId)?.RunId != runId)
                    continue;
                var view = await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
                var position = positions.Find(session.CharacterId);
                if (view is null || position is null)
                    throw new InvalidOperationException($"Cannot prepare party wipe return for {session.CharacterId}.");
                entries.Add(new TownAreaEntry(session, view, position));
            }
            var result = cleared
                ? await commands.SendAsync(new EndClearReturnCommand(runId, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false)
                : await commands.SendAsync(new EndPartyWipeCommand(runId, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
            if (result.Ended is not { } ended) return;
            foreach (var session in frozen)
            {
                cards.Cancel(session.CharacterId, runId);
                registry.RemoveParticipant(runId, session.CharacterId);
            }
            // Keep the party intact. Automatic failure ends the instance, not party membership.
            // No unsolicited 002A/007B ACK: the existing town projection provides 0003/0017/0018.
            foreach (var entry in entries.Where(x => ended.Participants.Any(p => p.CharacterId == x.Session.CharacterId)))
            {
                try
                {
                    await towns.EnterAsync([entry], cancellationToken).ConfigureAwait(false);
                    logger.LogInformation("Dungeon returned character={CharacterId} run={RunId} reason={Reason}", entry.Session.CharacterId, runId, cleared ? "clear-timeout" : "party-wipe");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Party wipe town projection failed character={CharacterId} run={RunId}", entry.Session.CharacterId, runId);
                }
            }
        }
        finally
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }
}
