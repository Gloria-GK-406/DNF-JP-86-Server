using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

internal sealed class GmAccessDomainService(IGmAdministrationPersistence persistence,
    IGmAuditDomainService audit, TimeProvider clock) : IGmAccessDomainService
{
    public ValueTask<bool> IsInitializedAsync(CancellationToken cancellationToken) => persistence.HasAdministratorAsync(cancellationToken);
    public async ValueTask<bool> BootstrapAsync(string loginName, string? passwordRecord, GmPermission permissions, CancellationToken cancellationToken)
    {
        await persistence.AcquireWriteIntentAsync(cancellationToken);
        if (await persistence.HasAdministratorAsync(cancellationToken)) return false;
        if (!ValidName(loginName) || passwordRecord is null || permissions == GmPermission.None
            || (permissions & ~GmPermission.All) != 0) return false;
        await persistence.CreateAdministratorAsync(loginName, passwordRecord, permissions, cancellationToken);
        return true;
    }

    public async ValueTask<GmLoginResult> LoginAsync(GmCredentialCheck check, CancellationToken cancellationToken)
    {
        await persistence.AcquireWriteIntentAsync(cancellationToken);
        var administrator = ValidName(check.LoginName) ? await persistence.FindAdministratorAsync(check.LoginName, cancellationToken) : null;
        // The check was made off the executor; it only counts for the administrator and record it saw.
        var accepted = check.Accepted && administrator is { Enabled: true }
            && administrator.Id == check.AdministratorId && administrator.PasswordRecord == check.Record;
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"), clock.GetUtcNow(), "Login", administrator?.Id,
            administrator?.LoginName, "GM", accepted ? "Succeeded" : "Rejected", accepted ? "管理员登录" : "凭据无效"), cancellationToken);
        if (!accepted) return new(null, null);
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = clock.GetUtcNow().AddHours(8);
        await persistence.SaveSessionAsync(new(Hash(secret), administrator!.Id, expires), cancellationToken);
        return new(new(administrator.Id, administrator.LoginName, administrator.Permissions, expires), secret);
    }

    public async ValueTask<GmIdentity?> IdentifyAsync(string? sessionSecret, CancellationToken cancellationToken)
    {
        if (!ValidSecret(sessionSecret)) return null;
        var session = await persistence.FindSessionAsync(Hash(sessionSecret!), cancellationToken);
        if (session is null || session.ExpiresAt <= clock.GetUtcNow()) return null;
        var administrator = await persistence.FindAdministratorAsync(session.AdministratorId, cancellationToken);
        return administrator is { Enabled: true } ? new(administrator.Id, administrator.LoginName, administrator.Permissions, session.ExpiresAt) : null;
    }

    public async ValueTask<bool> LogoutAsync(string sessionSecret, CancellationToken cancellationToken)
    {
        await persistence.AcquireWriteIntentAsync(cancellationToken);
        var identity = await IdentifyAsync(sessionSecret, cancellationToken);
        if (identity is null) return false;
        await persistence.DeleteSessionAsync(Hash(sessionSecret), cancellationToken);
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"), clock.GetUtcNow(), "Logout", identity.Id,
            identity.LoginName, "GM", "Succeeded", "管理员退出"), cancellationToken);
        return true;
    }

    internal static bool ValidName(string name) => name.Length is >= 1 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    private static bool ValidSecret(string? secret) => secret is { Length: 64 } && secret.All(char.IsAsciiHexDigit);
    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));
}

internal sealed class GmAuditDomainService(IGmAdministrationPersistence persistence) : IGmAuditDomainService
{
    public ValueTask AppendAsync(GmAuditRecord record, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(record.OperationId) || record.Action.Length is < 1 or > 64
            || record.Target.Length > 256 || record.Outcome.Length is < 1 or > 32 || record.Reason.Length > 512
            || !ValidActor(record))
            throw new ArgumentException("Invalid GM audit record.");
        return persistence.AppendAuditAsync(record, cancellationToken);
    }

    /// <remarks>
    /// The one write path of every audit record keeps the actor and the account together: an administrator's record
    /// has no account, a player's has its account. The only player record without one is a refused login whose name
    /// matched no account, and that record names nobody (RQ-024: the submitted name may be a mistyped password).
    /// </remarks>
    private static bool ValidActor(GmAuditRecord record) => record.ActorKind switch
    {
        GmAuditActors.Gm => record.AccountId is null && !GmAuditActors.PlayerActions.Contains(record.Action),
        GmAuditActors.Player => record.AdministratorId is null && !string.IsNullOrEmpty(record.AdministratorName)
            && GmAuditActors.PlayerActions.Contains(record.Action)
            && (record.AccountId is > 0
                || (record.AccountId is null && record.Action == GmAuditActors.PlayerLogin && record.Outcome == "Rejected"
                    && record.AdministratorName == GmAuditActors.UnrecordedPlayerName)),
        _ => false,
    };
    public ValueTask<GmAuditPage> QueryAsync(int page, int pageSize, GmAuditFilter filter, CancellationToken cancellationToken)
    {
        if (page < 1 || page > 1000000 || pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(page));
        if (!filter.IsValid) throw new ArgumentException("Invalid GM audit filter.", nameof(filter));
        return persistence.QueryAuditAsync(page, pageSize, filter, cancellationToken);
    }
}
