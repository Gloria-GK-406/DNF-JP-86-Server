using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Application.Users;
using System.Security.Cryptography;
using System.Text;

namespace Dfo.Server.Application.Authentication;

internal sealed class AuthenticationDomainService(
    IAuthenticationPersistence persistence, IPasswordCredentialRuleService passwords,
    TimeProvider clock, AuthenticationRuleService rules) : IAuthenticationDomainService
{
    public async ValueTask<RegistrationResult> RegisterAsync(string loginId, string password, CancellationToken cancellationToken)
    {
        var errors = rules.Validate(loginId, password, registration: true);
        if (errors.Count != 0) return new(AuthenticationStatus.InvalidRequest, FieldErrors: errors);
        var accountId = await persistence.TryCreateAsync(loginId, passwords.Hash(password), cancellationToken);
        return accountId.HasValue ? new(AuthenticationStatus.Success, loginId) : new(AuthenticationStatus.AccountExists);
    }

    public async ValueTask<AuthenticationResult> AuthenticateAndIssueTicketAsync(string loginId, string password, CancellationToken cancellationToken)
    {
        var errors = rules.Validate(loginId, password, registration: false);
        if (errors.Count != 0) return new(AuthenticationStatus.InvalidRequest, FieldErrors: errors);
        await persistence.AcquireWriteIntentAsync(cancellationToken);
        var account = await persistence.FindAsync(loginId, cancellationToken);
        if (account is null)
        {
            passwords.VerifyDummy(password);
            return new(AuthenticationStatus.InvalidCredentials);
        }
        var check = passwords.Verify(account.PasswordHash, password);
        var migrate = check == PasswordCheck.Failed && rules.MayMigrate(account.AccountId)
            && PasswordCredentialService.VerifyLegacyMd5(account.PasswordHash, password);
        if (check == PasswordCheck.Failed && !migrate)
            return new(AuthenticationStatus.InvalidCredentials);
        if ((migrate || check == PasswordCheck.RehashNeeded)
            && !await persistence.TryReplaceCredentialAsync(account.AccountId, account.PasswordHash, passwords.Hash(password), cancellationToken))
            return new(AuthenticationStatus.InvalidCredentials);
        var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = clock.GetUtcNow();
        var expiresAt = now.AddSeconds(60);
        await persistence.InsertTicketAsync(Digest(ticket), account.AccountId, now, expiresAt, cancellationToken);
        return new(AuthenticationStatus.Success, account.LoginId, ticket, expiresAt);
    }

    public async ValueTask<AccountId?> ConsumeTicketAsync(string loginId, string ticket, CancellationToken cancellationToken)
    {
        if (ticket is null || ticket.Length != 43 || ticket.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
            return null;
        // Waiting for another writer may outlive the ticket. Read time only after holding the lock.
        await persistence.AcquireWriteIntentAsync(cancellationToken);
        return await persistence.TryConsumeTicketAsync(loginId, Digest(ticket), clock.GetUtcNow(), cancellationToken);
    }
    private static string Digest(string ticket) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(ticket)));
}

internal sealed class GameLoginAuthenticationDomainService(
    IAuthenticationDomainService authentication,
    OnlineStateRegistry onlineUsers) : IGameLoginAuthenticationDomainService
{
    public async ValueTask<AccountId?> RedeemTicketAsync(
        OnlineUserId onlineUserId,
        string loginId,
        string ticket,
        CancellationToken cancellationToken)
    {
        var accountId = await authentication.ConsumeTicketAsync(
            loginId,
            ticket,
            cancellationToken).ConfigureAwait(false);
        if (accountId is null)
        {
            return null;
        }

        onlineUsers.PrepareAuthentication(onlineUserId, accountId.Value);
        return accountId;
    }
}
