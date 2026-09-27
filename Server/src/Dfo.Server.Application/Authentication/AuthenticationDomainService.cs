using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Application.Users;

namespace Dfo.Server.Application.Authentication;

internal sealed class AuthenticationDomainService(
    IAuthenticationPersistence persistence, IPasswordCredentialRuleService passwords,
    TimeProvider clock, AuthenticationRuleService rules,
    LoginSessionRegistry sessions, LoginSessionOptions sessionOptions) : IAuthenticationDomainService
{
    private const int MIdEncodedLength = 6;
    private const int TicketEncodedLength = 32;

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
        var now = clock.GetUtcNow();
        var expiresAt = now.AddSeconds(sessionOptions.InitialLifetimeSeconds);
        var session = sessions.Issue(account.AccountId, now, expiresAt);
        return new(AuthenticationStatus.Success, session.MId, session.Ticket, session.ExpiresAtUtc);
    }

    public ValueTask<AccountId?> BindSessionAsync(Dfo.Server.Data.Networking.NetworkConnectionId connectionId, string mId, string ticket, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mId is null
            || mId.Length != MIdEncodedLength
            || mId.Any(character => !char.IsAsciiDigit(character))
            || ticket is null
            || ticket.Length != TicketEncodedLength
            || ticket.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'A' and <= 'F')))
        {
            return ValueTask.FromResult<AccountId?>(null);
        }

        return ValueTask.FromResult(sessions.TryBind(connectionId, mId, ticket));
    }
}

internal sealed class GameLoginAuthenticationDomainService(
    IAuthenticationDomainService authentication,
    OnlineStateRegistry onlineUsers) : IGameLoginAuthenticationDomainService
{
    public async ValueTask<AccountId?> AuthenticateSessionAsync(
        OnlineUserId onlineUserId,
        string mId,
        string ticket,
        CancellationToken cancellationToken)
    {
        var accountId = await authentication.BindSessionAsync(
            onlineUsers.GetRequiredUser(onlineUserId).Connection.ConnectionId,
            mId,
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
