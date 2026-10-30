using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Sessions;

public interface ISelectedSessionCleanupDomainService
{
    /// <summary>
    /// Every exact session a cleanup of <paramref name="session"/> may record effects for, read before it starts.
    /// The command mechanism holds the cleanup's footprint to this set; it is the only statement of who a departure reaches.
    /// </summary>
    IReadOnlySet<SelectedSessionIdentity> AffectedSessions(SelectedSessionIdentity session);
    ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
        SelectedSessionIdentity session, bool includeOwner, IEffectFootprintRecorder footprint, CancellationToken cancellationToken);
}

public interface ISelectCharacterSessionEntryDomainService
{
    ValueTask<TownParticipantPublicationPlan?> PlanTownPublicationAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken);
    ValueTask<PvpLobbySynchronizationState?> PlanPvpAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken);
    PvpParticipantPublicationPlan PlanPvpPublication(
        SelectedSessionIdentity session, SelectCharacterResult result);
    void Publish(SelectedSessionIdentity session, SelectCharacterResult result);
    void MarkPvpReady(SelectedSessionIdentity session, SelectCharacterResult result);
}
