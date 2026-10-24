using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Sessions;

public interface ISelectedSessionCleanupDomainService
{
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
