using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Tests.Support;

/// <summary>
/// The account side of one database, put together the way the server puts it together.
/// </summary>
/// <remarks>
/// Assembling an account is a domain service over several single-table repositories now, so a test
/// that wants one has to say so in the same terms. Its own transaction scope is here too, because
/// a write has to belong to a use case and a test is the one deciding where that use case begins.
/// </remarks>
internal sealed class AccountSide
{
    private readonly SqliteDatabase database;

    public AccountSide(string databasePath)
    {
        this.Scope = new DomainTransactionScope();
        this.database = new SqliteDatabase(databasePath, this.Scope);
    }

    public DomainTransactionScope Scope { get; }

    public IAccountRepository Provisioning => new AccountProvisioning(new SqliteAccountTable(this.database));

    public IAccountEntryStateRepository Entry => new AccountEntryAssembly(
        new SqliteAccountTable(this.database),
        new SqliteAccountSettingsTable(this.database),
        new PremiumServiceDomainService(new SqliteAccountPremiumTable(this.database), this.Scope, TimeProvider.System, null!, new DisabledDefaultPremiumSettings()),
        new SqliteAccountLotteryProgressTable(this.database),
        new SqliteCharacterTable(this.database),
        new SqliteCharacterConditionTable(this.database),
        new SqliteCharacterBuyRestrictTable(this.database),
        new SqliteCharacterMercenarySupportTable(this.database));

    /// <summary>Creates the account inside a use case, as logging in does.</summary>
    public async Task<AccountRecord> GetOrCreateAsync(string loginId, string passwordHash)
    {
        await using var lease = this.Scope.Begin();
        var account = await this.Provisioning
            .GetOrCreateAsync(loginId, passwordHash, CancellationToken.None);
        await lease.CommitAsync(CancellationToken.None);
        return account;
    }
}
