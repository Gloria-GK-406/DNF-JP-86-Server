using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Altar;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game;

internal static class AltarTransitionProjection
{
    internal static IReadOnlyList<SelectedSessionIdentity> Participants(IDungeonRunDomainService runs,
        ISelectedSessionDomainService sessions, IDungeonRunProjectionDomainService registry, DungeonRunId runId) =>
        registry.Find(runId).Where(x => sessions.IsInWorld(x) && runs.Find(x.CharacterId)?.RunId == runId).ToArray();

    internal static async ValueTask ProjectAsync(ICommandProjections projections, AltarActionResult action,
        IReadOnlyList<SelectedSessionIdentity> recipients, TimeProvider clock, IAltarSchedulerWakeSignal wake,
        ILogger logger, CancellationToken ct)
    {
        if (recipients.Count > 0)
        {
            try
            {
                var result = await projections.ProjectAsync(new NotificationAudience.TheseSessions(recipients),
                    new AltarStateChanged(action, clock.GetUtcNow()), ct).ConfigureAwait(false);
                if (result.Attempts.Any(x => x.Outcome == NotificationOutcome.Failed))
                    throw new InvalidOperationException("A committed altar transition could not be projected.");
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                throw new TransactionOutcomeUncertainException(TransactionUncertaintyReason.PostCommitPublicationFailure,
                    "Altar state committed but client publication failed.", error);
            }
        }
        if (action.RoundStarted || action.Spawns.Count > 0 || action.DifficultyPrompt || action.ShowRanking || action.ShowRewards || action.Awards.Count > 0)
            logger.LogInformation("Altar transition run={RunId} room={RoomVersion} round={Round} status={Status} waveActors={Actors} generation={Generation}",
                action.State.RunId, action.State.RoomVersion, action.State.CompletedRounds, action.State.Status, action.Spawns.Count, action.State.Generation);
        wake.Wake();
    }
}
