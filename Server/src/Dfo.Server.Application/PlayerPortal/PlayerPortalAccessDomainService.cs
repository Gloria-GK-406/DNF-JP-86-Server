using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.PlayerPortal;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Application.PlayerPortal;

/// <remarks>
/// Sessions are made like GM sessions: a random 32-byte secret for the cookie, only its SHA-256 stored, a fixed
/// lifetime that is never extended (RQ-005).
/// </remarks>
internal sealed class PlayerPortalAccessDomainService(IPlayerPortalPersistence portal, IAuthenticationPersistence accounts,
    IGmPlayerDirectoryPersistence directory, IGmAuditDomainService audit, TimeProvider clock) : IPlayerPortalAccessDomainService
{
    internal static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    public async ValueTask<bool> IsOpenAsync(CancellationToken token) => (await portal.ReadSettingsAsync(token)).Enabled;

    public async ValueTask<PlayerIdentity?> IdentifyAsync(string? secret, CancellationToken token)
    {
        if (!ValidSecret(secret)) return null;
        var session = await portal.FindSessionAsync(Hash(secret!), token);
        if (session is null || session.ExpiresAt <= clock.GetUtcNow()) return null;
        var account = (await directory.AccountsAsync(new GmPlayerFilter(PageSize: 1, AccountId: session.AccountId), null, token)).Items.FirstOrDefault();
        return account is null ? null : new(account.AccountId, account.LoginName, session.ExpiresAt);
    }

    public async ValueTask<PlayerIdentity> AuthorizeAsync(string? secret, CancellationToken token)
    {
        if (!await IsOpenAsync(token)) throw new PlayerPortalException(PlayerPortalRefusal.PortalClosed, "玩家后台已关闭。");
        return await IdentifyAsync(secret, token) ?? throw new PlayerPortalException(PlayerPortalRefusal.Unauthenticated, "请先登录。");
    }

    public async ValueTask<PlayerLoginResult> LoginAsync(PlayerCredentialCheck check, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(check);
        await portal.AcquireWriteAsync(token);
        // Closed between the password check and this CMD: no session, and like the boundary's refusal no audit (RQ-007).
        if (!(await portal.ReadSettingsAsync(token)).Enabled) return new(PlayerLoginStatus.PortalClosed);
        var account = AuthenticationInputRules.ValidLoginId(check.LoginName) ? await accounts.FindAsync(check.LoginName, token) : null;
        var now = clock.GetUtcNow();
        if (check.Login is { } login && account is not null && account.AccountId == login.AccountId && account.PasswordHash == login.VerifiedRecord)
        {
            // The launcher login stores rehashes and legacy migrations; the portal only reads the record (no write-back).
            await portal.DeleteExpiredSessionsAsync(now, token);
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var identity = new PlayerIdentity(account.AccountId.Value, account.LoginId, now + SessionLifetime);
            await portal.SaveSessionAsync(new(Hash(secret), identity.AccountId, now, identity.ExpiresAt), token);
            await audit.AppendAsync(PlayerPortalAudit.Player(GmAuditActors.PlayerLogin, identity.AccountId, identity.LoginName,
                PlayerPortalAudit.AccountTarget(identity.AccountId), "Succeeded", "玩家登录", now), token);
            return new(PlayerLoginStatus.Succeeded, identity, secret);
        }
        if (check.Login is not null && !check.FinalAttempt) return new(PlayerLoginStatus.CredentialChanged);
        await audit.AppendAsync(PlayerPortalAudit.RejectedLogin(account is null ? null : (account.AccountId.Value, account.LoginId), now), token);
        return new(PlayerLoginStatus.InvalidCredentials);
    }

    public async ValueTask LogoutAsync(string secret, CancellationToken token)
    {
        await portal.AcquireWriteAsync(token);
        var identity = await AuthorizeAsync(secret, token);
        await portal.DeleteSessionAsync(Hash(secret), token);
        await audit.AppendAsync(PlayerPortalAudit.Player("PlayerLogout", identity.AccountId, identity.LoginName,
            PlayerPortalAudit.AccountTarget(identity.AccountId), "Succeeded", "玩家退出", clock.GetUtcNow()), token);
    }

    private static bool ValidSecret(string? secret) => secret is { Length: 64 } && secret.All(char.IsAsciiHexDigit);
    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));
}
