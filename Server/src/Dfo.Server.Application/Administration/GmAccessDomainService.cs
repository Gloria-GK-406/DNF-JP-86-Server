using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

internal sealed class GmAccessDomainService(IGmAdministrationPersistence persistence,
    IPasswordCredentialRuleService passwords, IGmAuditDomainService audit, TimeProvider clock) : IGmAccessDomainService
{
    public ValueTask<bool> IsInitializedAsync(CancellationToken cancellationToken) => persistence.HasAdministratorAsync(cancellationToken);
    public async ValueTask<bool> BootstrapAsync(string loginName, string password, GmPermission permissions, CancellationToken cancellationToken)
    {
        await persistence.AcquireWriteIntentAsync(cancellationToken);
        if (await persistence.HasAdministratorAsync(cancellationToken)) return false;
        if (!ValidName(loginName) || password.Length is < 12 or > 256 || permissions == GmPermission.None
            || (permissions & ~GmPermission.All) != 0) return false;
        await persistence.CreateAdministratorAsync(loginName, passwords.Hash(password), permissions, cancellationToken);
        return true;
    }

    public async ValueTask<GmLoginResult> LoginAsync(string loginName, string password, CancellationToken cancellationToken)
    {
        await persistence.AcquireWriteIntentAsync(cancellationToken);
        var administrator = ValidName(loginName) ? await persistence.FindAdministratorAsync(loginName, cancellationToken) : null;
        var accepted = false;
        if (administrator is { Enabled: true }) accepted = passwords.Verify(administrator.PasswordRecord, password) != PasswordCheck.Failed;
        else passwords.VerifyDummy(password);
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

    private static bool ValidName(string name) => name.Length is >= 1 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    private static bool ValidSecret(string? secret) => secret is { Length: 64 } && secret.All(char.IsAsciiHexDigit);
    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));
}

internal sealed class GmAuditDomainService(IGmAdministrationPersistence persistence) : IGmAuditDomainService
{
    public ValueTask AppendAsync(GmAuditRecord record, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(record.OperationId) || record.Action.Length is < 1 or > 64
            || record.Target.Length > 256 || record.Outcome.Length is < 1 or > 32 || record.Reason.Length > 512)
            throw new ArgumentException("Invalid GM audit record.");
        return persistence.AppendAuditAsync(record, cancellationToken);
    }
    public ValueTask<GmAuditPage> QueryAsync(int page, int pageSize, GmAuditFilter filter, CancellationToken cancellationToken)
    {
        if (page < 1 || page > 1000000 || pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(page));
        if (!filter.IsValid) throw new ArgumentException("Invalid GM audit filter.", nameof(filter));
        return persistence.QueryAuditAsync(page, pageSize, filter, cancellationToken);
    }
}
