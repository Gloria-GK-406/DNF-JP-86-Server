using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.PlayerPortal;

/// <summary>
/// The player's audit records. Every player record is built here, so it always carries the player actor, the account
/// and its login name, and never an administrator.
/// </summary>
internal static class PlayerPortalAudit
{
    internal static string AccountTarget(int accountId) => $"账号 {accountId}";

    internal static GmAuditRecord Player(string action, int accountId, string loginName, string target, string outcome,
        string reason, DateTimeOffset at) =>
        new(Guid.NewGuid().ToString("N"), at, action, null, loginName, target, outcome, reason, GmAuditActors.Player, accountId);

    /// <summary>
    /// A refused login names the account only when the submitted name is an existing account's; otherwise neither the
    /// name nor an account is recorded (RQ-024), and the submitted password never is.
    /// </summary>
    internal static GmAuditRecord RejectedLogin((int Id, string LoginName)? account, DateTimeOffset at) => account is { } known
        ? Player(GmAuditActors.PlayerLogin, known.Id, known.LoginName, AccountTarget(known.Id), "Rejected", "凭据无效", at)
        : new(Guid.NewGuid().ToString("N"), at, GmAuditActors.PlayerLogin, null, GmAuditActors.UnrecordedPlayerName,
            "未知账号", "Rejected", "凭据无效", GmAuditActors.Player, null);
}
