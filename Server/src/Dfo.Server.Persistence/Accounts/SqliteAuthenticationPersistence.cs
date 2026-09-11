using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Accounts;

internal sealed class SqliteAuthenticationPersistence(SqliteDatabase database) : IAuthenticationPersistence
{
    // A zero-row write takes SQLite's writer reservation before the credential snapshot exists.
    // It belongs to the same participant and stays held through KDF and an optional credential
    // replacement. Runtime login sessions are intentionally not stored in this database.
    public async ValueTask AcquireWriteIntentAsync(CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync("UPDATE accounts SET m_id=m_id WHERE 0;", _ => { }, cancellationToken);
    public ValueTask<AccountRecord?> FindAsync(string loginId, CancellationToken cancellationToken) =>
        new SqliteAccountTable(database).FindByLoginIdAsync(loginId, cancellationToken);
    public async ValueTask<AccountId?> TryCreateAsync(string loginId, string passwordRecord, CancellationToken cancellationToken) =>
        (await database.WriteReturningAsync(
            "INSERT INTO accounts(m_id,password_hash) VALUES(@login,@record) ON CONFLICT(m_id) DO NOTHING RETURNING account_id;",
            p => { p.AddWithValue("@login", loginId); p.AddWithValue("@record", passwordRecord); },
            r => new Identity(new AccountId(r.GetInt32(0))), cancellationToken))?.Value;
    public async ValueTask<bool> TryReplaceCredentialAsync(AccountId accountId, string expected, string replacement, CancellationToken cancellationToken) =>
        await database.ExecuteAsync("UPDATE accounts SET password_hash=@replacement WHERE account_id=@aid AND password_hash=@expected;",
            p => { p.AddWithValue("@replacement", replacement); p.AddWithValue("@aid", accountId.Value); p.AddWithValue("@expected", expected); }, cancellationToken) == 1;
    private sealed record Identity(AccountId Value);
}
