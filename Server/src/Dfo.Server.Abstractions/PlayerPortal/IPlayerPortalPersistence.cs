using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Abstractions.PlayerPortal;

/// <summary>Storage of the player portal: its settings, player sessions and succeeded player operations.</summary>
/// <remarks>Every write must run inside the command's transaction, like the GM stores.</remarks>
public interface IPlayerPortalPersistence
{
    ValueTask AcquireWriteAsync(CancellationToken token);

    /// <summary>The saved settings, or <see cref="PlayerPortalSettings.Default"/> when none were ever saved.</summary>
    ValueTask<PlayerPortalSettings> ReadSettingsAsync(CancellationToken token);

    /// <summary>
    /// Saves <paramref name="settings"/> when the stored revision is <paramref name="expectedRevision"/> (zero for
    /// never saved); the stored revision becomes one more. Returns false when the revision did not match.
    /// <see cref="PlayerPortalSettings.Revision"/> of the argument is ignored.
    /// </summary>
    ValueTask<bool> SaveSettingsAsync(PlayerPortalSettings settings, long expectedRevision, CancellationToken token);

    ValueTask SaveSessionAsync(PlayerPortalSession session, CancellationToken token);
    ValueTask<PlayerPortalSession?> FindSessionAsync(string tokenHash, CancellationToken token);
    ValueTask<bool> DeleteSessionAsync(string tokenHash, CancellationToken token);
    ValueTask<int> DeleteAccountSessionsAsync(int accountId, CancellationToken token);
    ValueTask<int> DeleteAllSessionsAsync(CancellationToken token);
    ValueTask<int> DeleteExpiredSessionsAsync(DateTimeOffset now, CancellationToken token);

    /// <summary>Sessions expiring after <paramref name="now"/>, newest first, with the account's login name.</summary>
    ValueTask<IReadOnlyList<PlayerPortalSessionEntry>> ListActiveSessionsAsync(DateTimeOffset now, CancellationToken token);

    ValueTask<PlayerPortalOperation?> FindOperationAsync(int accountId, Guid requestId, CancellationToken token);

    /// <summary>Records a succeeded operation; a second one with the same account and request id throws.</summary>
    ValueTask SaveOperationAsync(PlayerPortalOperation operation, CancellationToken token);

    /// <summary>
    /// Succeeded operations of one account, kind and game day. For cera pass the currency: each currency has its own
    /// daily limit, so a null <paramref name="currency"/> (which counts every currency) is only for mail.
    /// </summary>
    ValueTask<PlayerPortalUsage> UsageAsync(int accountId, PlayerPortalOperationKind kind, string? currency, DateOnly periodDay,
        CancellationToken token);

    /// <summary>One account's operations, newest first, optionally of one kind.</summary>
    ValueTask<PlayerPortalOperationPage> OperationsAsync(int accountId, PlayerPortalOperationKind? kind, int page, int pageSize,
        CancellationToken token);
}
