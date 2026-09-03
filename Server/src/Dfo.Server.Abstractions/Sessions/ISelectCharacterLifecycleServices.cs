using Dfo.Server.Data.Accounts;
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

    /// <summary>The session this account currently has selected, or null when it has none.</summary>
    SelectedSessionIdentity? Find(AccountId accountId);

    /// <summary>
    /// Makes this the account's selected session, answering the one it displaced when there was one.
    /// </summary>
    SelectedSessionIdentity? RegisterReplacing(SelectedSessionIdentity session);

    /// <summary>
    /// Releases exactly this session, and answers whether it was still the current one.
    /// </summary>
    /// <remarks>
    /// Exact rather than by account: a session that has already been displaced must not take its
    /// replacement's registration down with it.
    /// </remarks>
    bool ReleaseExact(SelectedSessionIdentity session);
}
