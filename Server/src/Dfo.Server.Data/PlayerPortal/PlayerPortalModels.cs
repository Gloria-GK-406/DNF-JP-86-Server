namespace Dfo.Server.Data.PlayerPortal;

/// <summary>Per-account limits of the player portal; zero means unlimited.</summary>
public sealed record PlayerPortalLimits(long CeraPerGrant, long CeraPerDay, long TokenCeraPerGrant, long TokenCeraPerDay,
    long MailGoldPerMail, long MailsPerDay)
{
    public static PlayerPortalLimits None { get; } = new(0, 0, 0, 0, 0, 0);
}

/// <summary>
/// The server-wide player portal settings. <see cref="Revision"/> zero means nothing was ever saved, which reads as
/// <see cref="Default"/>: the portal closed, both features open, no limits.
/// </summary>
public sealed record PlayerPortalSettings(bool Enabled, bool CeraEnabled, bool MailEnabled, PlayerPortalLimits Limits, long Revision)
{
    public static PlayerPortalSettings Default { get; } = new(false, true, true, PlayerPortalLimits.None, 0);
}

public sealed record PlayerPortalSession(string TokenHash, int AccountId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>One live player session as an administrator sees it; the token hash is not part of it.</summary>
public sealed record PlayerPortalSessionEntry(int AccountId, string LoginName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public enum PlayerPortalOperationKind { Cera, Mail }

/// <summary>
/// One succeeded player operation. For <see cref="PlayerPortalOperationKind.Cera"/> <see cref="Currency"/> names the
/// currency and <see cref="Amount"/> is how much was added; for <see cref="PlayerPortalOperationKind.Mail"/>
/// <see cref="Currency"/> is null and <see cref="Amount"/> is the attached gold. <see cref="ReceiptJson"/> is opaque here.
/// </summary>
public sealed record PlayerPortalOperation(int AccountId, Guid RequestId, PlayerPortalOperationKind Kind, string? Currency, long Amount,
    string PayloadHash, string ReceiptJson, DateOnly PeriodDay, DateTimeOffset CommittedAt);

/// <summary>Succeeded operations of one game day: how many, and the sum of their amounts.</summary>
public sealed record PlayerPortalUsage(long Count, long Amount);

public sealed record PlayerPortalOperationPage(IReadOnlyList<PlayerPortalOperation> Items, long Total, int Page, int PageSize);
