using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Support;

/// <summary>Account entry services and explicit database seeding for persistence tests.</summary>
internal sealed class AccountSide
{
    private readonly SqliteDatabase database;

    public AccountSide(string databasePath)
    {
        this.Scope = new DomainTransactionScope();
        this.database = new SqliteDatabase(databasePath, this.Scope);
    }

    public DomainTransactionScope Scope { get; }

    public IAccountEntryStateRepository Entry => new AccountEntryAssembly(
        new SqliteAccountTable(this.database),
        new SqliteAccountSettingsTable(this.database),
        new PremiumServiceDomainService(new SqliteAccountPremiumTable(this.database), this.Scope, TimeProvider.System, null!, new DisabledDefaultPremiumSettings()),
        new SqliteAccountLotteryProgressTable(this.database),
        new SqliteCharacterTable(this.database),
        new SqliteCharacterConditionTable(this.database),
        new SqliteCharacterBuyRestrictTable(this.database),
        new SqliteCharacterMercenarySupportTable(this.database));

    /// <summary>Seeds a fixture account in a transaction; this is not the login path.</summary>
    public async Task<AccountRecord> GetOrCreateAsync(string loginId, string passwordHash)
    {
        return await TestGameplay.RunAsync(async () =>
        {
            await using var lease = this.Scope.Begin();
            await this.database.ExecuteAsync(
                "INSERT INTO accounts(m_id,password_hash) VALUES(@login,@hash) ON CONFLICT(m_id) DO NOTHING;",
                parameters => { parameters.AddWithValue("@login", loginId); parameters.AddWithValue("@hash", passwordHash); },
                CancellationToken.None);
            var account = await new SqliteAccountTable(this.database).FindByLoginIdAsync(loginId, CancellationToken.None)
                ?? throw new InvalidOperationException("Fixture account was not seeded.");
            await lease.CommitAsync(CancellationToken.None);
            return account;
        });
    }
}
