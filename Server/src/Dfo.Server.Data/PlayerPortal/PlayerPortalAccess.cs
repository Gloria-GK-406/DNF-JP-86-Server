using Dfo.Server.Data.Authentication;

namespace Dfo.Server.Data.PlayerPortal;

/// <summary>A player signed in to the player portal: an account and nothing more (no administrator rights).</summary>
/// <remarks><see cref="LoginName"/> is the account's stored login name, read when the session is identified.</remarks>
public sealed record PlayerIdentity(int AccountId, string LoginName, DateTimeOffset ExpiresAt);

/// <summary>
/// A player login checked off the executor. <see cref="Login"/> is set only when the password matched the record it
/// names; the login CMD trusts it only while that record is still the stored one. On <see cref="FinalAttempt"/> a
/// changed record is refused instead of being handed back for another check.
/// </summary>
public sealed record PlayerCredentialCheck(string LoginName, VerifiedLogin? Login, bool FinalAttempt)
{
    public override string ToString() => $"PlayerCredentialCheck {{ Verified = {Login is not null}, FinalAttempt = {FinalAttempt} }}";
}

/// <summary><see cref="CredentialChanged"/> never reaches a client: the endpoint checks the password once more.</summary>
public enum PlayerLoginStatus { Succeeded, InvalidCredentials, PortalClosed, CredentialChanged }

public sealed record PlayerLoginResult(PlayerLoginStatus Status, PlayerIdentity? Identity = null, string? SessionSecret = null)
{
    public override string ToString() => $"PlayerLoginResult {{ Status = {Status} }}";
}

/// <summary>Why the player portal refused a request; each maps to one response code of the player API.</summary>
/// <remarks><see cref="SettingsConflict"/> is the administrator's stale settings revision, never a player's refusal.</remarks>
public enum PlayerPortalRefusal { PortalClosed, FeatureDisabled, Unauthenticated, NotFound, LimitExceeded, RequestConflict, Invalid, SettingsConflict }

public sealed class PlayerPortalException(PlayerPortalRefusal reason, string message) : InvalidOperationException(message)
{
    public PlayerPortalRefusal Reason { get; } = reason;
}

/// <summary>An administrator's settings save; <see cref="Revision"/> is the revision it was read at (zero: never saved).</summary>
public sealed record PlayerSettingsChange(bool Enabled, bool CeraEnabled, bool MailEnabled, PlayerPortalLimits? Limits, long Revision);

/// <summary>What a player may read about the own account (RQ-003), with the portal rules that apply to it.</summary>
public sealed record PlayerAccountView(int AccountId, string LoginName, PlayerBalancesView Balances,
    IReadOnlyList<PlayerCharacterView> Characters, PlayerPortalView Portal);
public sealed record PlayerBalancesView(long Cera, long TokenCera);
public sealed record PlayerCharacterView(int CharacterId, string Name, int Job, int Level, bool IsOnline);
public sealed record PlayerPortalView(bool CeraEnabled, bool MailEnabled, PlayerPortalLimits Limits, PlayerUsageView Today);
/// <summary>Succeeded use of the current game day: currency amounts and the number of mails.</summary>
public sealed record PlayerUsageView(long Cera, long TokenCera, long Mails);
public sealed record PlayerSessionsKicked(int Kicked);
