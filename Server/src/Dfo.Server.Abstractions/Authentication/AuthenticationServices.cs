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

/// <summary>
/// The launcher's credential half that runs off the executor (RQ-007): validation, the account read
/// and every hash or verification. Its results are the inputs of the registration and login CMDs.
/// </summary>
public interface ILauncherCredentialReader
{
    ValueTask<RegistrationPreparation> PrepareRegistrationAsync(string loginId, string password, CancellationToken cancellationToken);
    ValueTask<LoginVerification> VerifyLoginAsync(string loginId, string password, CancellationToken cancellationToken);
}

public interface IAuthenticationDomainService
{
    ValueTask<RegistrationResult> RegisterAsync(PreparedRegistration registration, CancellationToken cancellationToken);
    /// <summary>Returns <see cref="AuthenticationStatus.CredentialChanged"/> when the verified record is no longer the stored one.</summary>
    ValueTask<AuthenticationResult> AuthenticateAndIssueTicketAsync(VerifiedLogin login, CancellationToken cancellationToken);
    ValueTask<AccountId?> BindSessionAsync(Dfo.Server.Data.Networking.NetworkConnectionId connectionId, string mId, string ticket, CancellationToken cancellationToken);
}

public interface IGameLoginAuthenticationDomainService
{
    ValueTask<AccountId?> AuthenticateSessionAsync(
        OnlineUserId onlineUserId,
        string mId,
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
}
