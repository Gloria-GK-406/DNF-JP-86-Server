using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Accounts;

/// <summary>
/// The account a login name refers to, made if this is the first time it has been seen.
/// </summary>
/// <remarks>
/// Two statements against one table, and the decision to run them in that order. The order is the
/// interesting part -- add unless already there, then read back what is there -- and it is a
/// decision about accounts, not about rows, so it is made here rather than inside the table.
///
/// Both statements settle with whatever else the login is doing, because the table reaches the
/// database through the use case rather than through a connection of its own.
/// </remarks>
internal sealed class AccountProvisioning(IAccountTable accounts) : IAccountRepository
{
    public async ValueTask<AccountRecord> GetOrCreateAsync(
        string loginId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(loginId) || loginId.Length > 256)
        {
            throw new ArgumentException(
                "Login ID must contain between 1 and 256 characters.", nameof(loginId));
        }

        passwordHash ??= string.Empty;
        if (passwordHash.Length > 256)
        {
            throw new ArgumentException(
                "Password hash cannot exceed 256 characters.", nameof(passwordHash));
        }

        await accounts.InsertIfAbsentAsync(loginId, passwordHash, cancellationToken)
            .ConfigureAwait(false);
        return await accounts.FindByLoginIdAsync(loginId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Account '{loginId}' could not be resolved after creation.");
    }
}
