using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

/// <remarks>
/// The administrator read is a plain read outside any transaction (spec appendix B, read-only SQLite);
/// the login CMD reads the administrator again and accepts the check only while it still applies.
/// </remarks>
internal sealed class GmCredentialReader(IGmAdministrationPersistence persistence, IPasswordCredentialRuleService passwords,
    PasswordComputationLimiter limiter) : IGmCredentialReader
{
    public async ValueTask<GmCredentialCheck> CheckLoginAsync(string loginName, string password, CancellationToken cancellationToken)
    {
        var administrator = GmAccessDomainService.ValidName(loginName)
            ? await persistence.FindAdministratorAsync(loginName, cancellationToken).ConfigureAwait(false)
            : null;
        var accepted = await limiter.RunAsync(() =>
        {
            if (administrator is { Enabled: true }) return passwords.Verify(administrator.PasswordRecord, password) != PasswordCheck.Failed;
            passwords.VerifyDummy(password);
            return false;
        }, cancellationToken).ConfigureAwait(false);
        return new(loginName, administrator?.Id, administrator?.PasswordRecord, accepted);
    }

    public async ValueTask<string?> PrepareBootstrapAsync(string password, CancellationToken cancellationToken) =>
        password.Length is < 12 or > 256
            ? null
            : await limiter.RunAsync(() => passwords.Hash(password), cancellationToken).ConfigureAwait(false);
}
