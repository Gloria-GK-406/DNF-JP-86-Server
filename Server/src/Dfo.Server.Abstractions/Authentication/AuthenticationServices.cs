using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Abstractions.Authentication;

public enum PasswordCheck { Failed, Verified, RehashNeeded }

public interface IPasswordCredentialRuleService
{
    string Hash(string password);
    PasswordCheck Verify(string record, string password);
    void VerifyDummy(string password);
}

public interface IAuthenticationDomainService
{
    ValueTask<RegistrationResult> RegisterAsync(string loginId, string password, CancellationToken cancellationToken);
    ValueTask<AuthenticationResult> AuthenticateAndIssueTicketAsync(string loginId, string password, CancellationToken cancellationToken);
    ValueTask<AccountId?> ConsumeTicketAsync(string loginId, string ticket, CancellationToken cancellationToken);
}

public interface IGameLoginAuthenticationDomainService
{
    ValueTask<AccountId?> RedeemTicketAsync(
        OnlineUserId onlineUserId,
        string loginId,
        string ticket,
        CancellationToken cancellationToken);
}

public interface IAuthenticationPersistence
{
    // Must precede every account read in an authentication transaction: SQLite cannot upgrade
    // a deferred snapshot after another writer commits. The intent is held through commit.
    ValueTask AcquireWriteIntentAsync(CancellationToken cancellationToken);
    ValueTask<AccountRecord?> FindAsync(string loginId, CancellationToken cancellationToken);
    ValueTask<AccountId?> TryCreateAsync(string loginId, string passwordRecord, CancellationToken cancellationToken);
    ValueTask<bool> TryReplaceCredentialAsync(AccountId accountId, string expected, string replacement, CancellationToken cancellationToken);
    ValueTask InsertTicketAsync(string digest, AccountId accountId, DateTimeOffset issuedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    ValueTask<AccountId?> TryConsumeTicketAsync(string loginId, string digest, DateTimeOffset now, CancellationToken cancellationToken);
}
