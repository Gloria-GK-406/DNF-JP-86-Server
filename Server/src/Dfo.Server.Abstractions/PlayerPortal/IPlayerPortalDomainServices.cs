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
