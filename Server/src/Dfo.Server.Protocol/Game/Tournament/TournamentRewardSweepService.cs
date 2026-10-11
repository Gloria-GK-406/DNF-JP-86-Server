using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Tournament;

/// <summary>The client leaves the flip UI on a timer without selecting the remaining cards.</summary>
public sealed class TournamentRewardSweepService(ITournamentDungeonDomainService tournaments,
    ISelectedSessionDomainService sessions, IDungeonRunDomainService runs, ICommandBus commands,
    DungeonProjectionGate projections, SelectedSessionTransitionGate transitions,
    TournamentProjectionDelivery delivery, IGameMessageSender sender, TimeProvider clock,
    ILogger<TournamentRewardSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                foreach (var state in tournaments.PendingRewards(clock.GetUtcNow()))
                {
                    try { await SweepAsync(state, stoppingToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception error) { logger.LogError(error, "Tournament automatic reward failed run={RunId}", state.RunId); }
                }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal async ValueTask SweepAsync(TournamentDungeonState candidate, CancellationToken ct)
    {
        var session = sessions.FindByCharacter(candidate.CharacterId);
        if (session is null) return;
        await using var projection = await projections.AcquireAsync(candidate.RunId, UserAccessMode.Exclusive, ct).ConfigureAwait(false);
        await using var transition = await transitions.AcquireAsync(session.AccountId, UserAccessMode.Exclusive, ct).ConfigureAwait(false);
        var current = tournaments.Find(candidate.CharacterId);
        if (sessions.FindByCharacter(candidate.CharacterId) != session || current?.RunId != candidate.RunId
            || runs.Find(candidate.CharacterId)?.RunId != candidate.RunId
            || current.RewardSelectionEndsAt is not { } deadline || deadline > clock.GetUtcNow()) return;
        for (byte row = 0; row < current.Rewards.Count; row++)
        {
            if (current.Rewards[row].Count == 0 || current.Selections[row].HasValue) continue;
            var result = await commands.SendAsync(new SelectTournamentRewardCommand(session, current.RunId, row, 0), ct).ConfigureAwait(false);
            if (result.Action is not { } action) continue;
            current = action.State;
            var slot = runs.Find(candidate.CharacterId)?.Participant?.Slot ?? 0;
            await sender.SendExplicitAsync(session.ConnectionId,
                new([.. delivery.CompletionFrames(action), new TournamentSelectionResponse(current, slot)], []), ct).ConfigureAwait(false);
        }
    }
}
