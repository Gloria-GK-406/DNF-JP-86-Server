using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Authentication;

namespace Dfo.Server.Application.Authentication;

/// <remarks>
/// The account read is a plain read outside any transaction (spec appendix B, read-only SQLite); the
/// login CMD reads the record again under its write intent and refuses to trust a changed one.
/// </remarks>
internal sealed class LauncherCredentialReader(
    IAuthenticationPersistence persistence, IPasswordCredentialRuleService passwords,
    AuthenticationRuleService rules, PasswordComputationLimiter limiter) : ILauncherCredentialReader
{
    public async ValueTask<RegistrationPreparation> PrepareRegistrationAsync(string loginId, string password, CancellationToken cancellationToken)
    {
        var errors = rules.Validate(loginId, password, registration: true);
        if (errors.Count != 0) return new(null, errors);
        var record = await limiter.RunAsync(() => passwords.Hash(password), cancellationToken).ConfigureAwait(false);
        return new(new PreparedRegistration(loginId, record));
    }

    public async ValueTask<LoginVerification> VerifyLoginAsync(string loginId, string password, CancellationToken cancellationToken)
    {
        var errors = rules.Validate(loginId, password, registration: false);
        if (errors.Count != 0) return new(AuthenticationStatus.InvalidRequest, FieldErrors: errors);
        var account = await persistence.FindAsync(loginId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            await limiter.RunAsync(() =>
            {
                passwords.VerifyDummy(password);
                return true;
            }, cancellationToken).ConfigureAwait(false);
            return new(AuthenticationStatus.InvalidCredentials);
        }

        var (accepted, replacement) = await limiter.RunAsync(() =>
        {
            var check = passwords.Verify(account.PasswordHash, password);
            var migrate = check == PasswordCheck.Failed && rules.MayMigrate(account.AccountId)
                && PasswordCredentialService.VerifyLegacyMd5(account.PasswordHash, password);
            if (check == PasswordCheck.Failed && !migrate) return (false, null);
            return (true, migrate || check == PasswordCheck.RehashNeeded ? passwords.Hash(password) : (string?)null);
        }, cancellationToken).ConfigureAwait(false);
        return accepted
            ? new(AuthenticationStatus.Success, new VerifiedLogin(loginId, account.AccountId, account.PasswordHash, replacement))
            : new(AuthenticationStatus.InvalidCredentials);
    }
}
