using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Application.Users;

namespace Dfo.Server.Application.Authentication;

internal sealed class AuthenticationDomainService(
    IAuthenticationPersistence persistence, TimeProvider clock,
    LoginSessionRegistry sessions, LoginSessionOptions sessionOptions, Func<Dfo.Server.Abstractions.Initialization.IInitializationDomainService> initialization) : IAuthenticationDomainService
{
    private const int MIdEncodedLength = 6;
    private const int TicketEncodedLength = 32;

    public async ValueTask<RegistrationResult> RegisterAsync(PreparedRegistration registration, CancellationToken cancellationToken)
    {
        var accountId = await persistence.TryCreateAsync(registration.LoginId, registration.PasswordRecord, cancellationToken);
        if (accountId.HasValue) await initialization().ApplyAccountAsync(accountId.Value, cancellationToken);
        return accountId.HasValue ? new(AuthenticationStatus.Success, registration.LoginId) : new(AuthenticationStatus.AccountExists);
    }

    public async ValueTask<AuthenticationResult> AuthenticateAndIssueTicketAsync(VerifiedLogin login, CancellationToken cancellationToken)
    {
        await persistence.AcquireWriteIntentAsync(cancellationToken);
        var account = await persistence.FindAsync(login.LoginId, cancellationToken);
        if (account is null || account.AccountId != login.AccountId || account.PasswordHash != login.VerifiedRecord)
            return new(AuthenticationStatus.CredentialChanged);
        if (login.ReplacementRecord is { } replacement
            && !await persistence.TryReplaceCredentialAsync(account.AccountId, login.VerifiedRecord, replacement, cancellationToken))
            return new(AuthenticationStatus.CredentialChanged);
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
