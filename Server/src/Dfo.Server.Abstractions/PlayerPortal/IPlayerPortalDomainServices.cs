using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Abstractions.PlayerPortal;

/// <summary>The player's identity in the portal: login, sessions and the per-request checks every player CMD makes.</summary>
public interface IPlayerPortalAccessDomainService
{
    /// <summary>Whether the portal's master switch is on; a plain read, used by the boundary and the status route.</summary>
    ValueTask<bool> IsOpenAsync(CancellationToken token);

    /// <summary>The live session's player, or null; a plain read, so a CMD checks again with <see cref="AuthorizeAsync"/>.</summary>
    ValueTask<PlayerIdentity?> IdentifyAsync(string? secret, CancellationToken token);

    /// <summary>
    /// Inside a CMD: the master switch first (<see cref="PlayerPortalRefusal.PortalClosed"/>), then the session
    /// (<see cref="PlayerPortalRefusal.Unauthenticated"/>), as the boundary orders them (RQ-021).
    /// </summary>
    ValueTask<PlayerIdentity> AuthorizeAsync(string? secret, CancellationToken token);

    ValueTask<PlayerLoginResult> LoginAsync(PlayerCredentialCheck check, CancellationToken token);
    ValueTask LogoutAsync(string secret, CancellationToken token);
}

public interface IPlayerPortalAccountDomainService
{
    ValueTask<PlayerAccountView> MeAsync(string secret, CancellationToken token);

    /// <summary>The session's own succeeded operations, newest first, optionally of one kind (RQ-013).</summary>
    ValueTask<PlayerOperationsView> OperationsAsync(string secret, PlayerPortalOperationKind? kind, int page, int pageSize, CancellationToken token);
}

/// <summary>A player adding cera to the own account (RQ-010).</summary>
public interface IPlayerPortalCeraDomainService
{
    /// <summary>
    /// Inside the CMD's execution unit: every check of SD-1 in order and the credit. A request id that already
    /// succeeded with the same content answers with the stored receipt, before the feature switch and the limits.
    /// </summary>
    ValueTask<PlayerCeraGrantResult> GrantAsync(string? secret, PlayerCeraRequest request, CancellationToken token);

    /// <summary>A failed grant's audit, under the identity found when the CMD began; the session is not read again (SD-1a).</summary>
    ValueTask RecordFailureAsync(PlayerIdentity identity, Guid requestId, string outcome, string reason, CancellationToken token);
}

/// <summary>A player mailing a character of the own account (RQ-011), and the item catalog that mail picks from (SD-8a).</summary>
public interface IPlayerPortalMailDomainService
{
    /// <summary>
    /// Off the executor, for the catalog, icon and tooltip reads: the master switch, the session, then the mail switch
    /// (<see cref="PlayerPortalRefusal.FeatureDisabled"/>).
    /// </summary>
    ValueTask<PlayerIdentity> AuthorizeCatalogAsync(string? secret, CancellationToken token);

    /// <summary>The GM mail catalog's search, with the same selection rule, so whatever is found can be sent.</summary>
    ValueTask<ItemCatalogPage> SearchItemsAsync(string? secret, ItemCatalogFilter filter, CancellationToken token);
    ValueTask<ItemCatalogDetails> ItemDetailsAsync(string? secret, int itemId, CancellationToken token);

    /// <summary>
    /// Inside the CMD's execution unit: every check of SD-1 in order and the delivery. A request id that already
    /// succeeded with the same content answers with the stored receipt, before the mail switch, the recipient and the limits.
    /// </summary>
    ValueTask<PlayerMailSendResult> SendAsync(string? secret, PlayerMailRequest request, CancellationToken token);

    /// <summary>A failed mail's audit, under the identity found when the CMD began; the session is not read again (SD-1a).</summary>
    ValueTask RecordFailureAsync(PlayerIdentity identity, Guid requestId, int characterId, string outcome, string reason, CancellationToken token);
}

/// <summary>The administrator's controls of the player portal; each call authorizes a full-permission GM session.</summary>
public interface IPlayerPortalAdministrationDomainService
{
    ValueTask<PlayerPortalSettings> SettingsAsync(string gmSecret, CancellationToken token);
    ValueTask<PlayerPortalSettings> ChangeSettingsAsync(string gmSecret, PlayerSettingsChange change, CancellationToken token);
    ValueTask<IReadOnlyList<PlayerPortalSessionEntry>> SessionsAsync(string gmSecret, CancellationToken token);

    /// <summary>Ends every session of <paramref name="accountId"/>, or of every player when null; returns how many.</summary>
    ValueTask<int> KickAsync(string gmSecret, int? accountId, CancellationToken token);
}
