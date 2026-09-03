using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Sessions;

public interface ISelectedSessionCleanupDomainService
{
    ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
        SelectedSessionIdentity session, bool includeOwner, CancellationToken cancellationToken);
}

public interface ISelectCharacterSessionEntryDomainService
{
    PvpLobbySynchronizationState? PlanPvp(SelectedSessionIdentity session, SelectCharacterResult result);
    IReadOnlyList<PvpParticipantPublicationTarget> PlanPvpPublication(
        SelectedSessionIdentity session, SelectCharacterResult result);
    void Publish(SelectedSessionIdentity session, SelectCharacterResult result);
    void ReplaceRuntimeExact(SelectedSessionIdentity session, SelectCharacterResult result);
    void MarkPvpReady(SelectedSessionIdentity session, SelectCharacterResult result);
}
