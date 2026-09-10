using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Accounts;

internal sealed class SqliteAuthenticationPersistence(SqliteDatabase database) : IAuthenticationPersistence
{
    // A zero-row write takes SQLite's writer reservation before the credential snapshot exists.
    // It belongs to the same participant and stays held through KDF, ticket insertion and commit.
    // This is local to authentication; unrelated read-only game transactions remain deferred.
    public async ValueTask AcquireWriteIntentAsync(CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync("UPDATE login_tickets SET consumed_at=consumed_at WHERE 0;", _ => { }, cancellationToken);
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
    public async ValueTask InsertTicketAsync(string digest, AccountId accountId, DateTimeOffset issuedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync("INSERT INTO login_tickets(ticket_digest,account_id,issued_at,expires_at) VALUES(@digest,@aid,@issued,@expires);",
            p =>
            {
                p.AddWithValue("@digest", digest); p.AddWithValue("@aid", accountId.Value);
                p.AddWithValue("@issued", issuedAt.UtcTicks); p.AddWithValue("@expires", expiresAt.UtcTicks);
            }, cancellationToken);
    public async ValueTask<AccountId?> TryConsumeTicketAsync(string loginId, string digest, DateTimeOffset now, CancellationToken cancellationToken) =>
        (await database.WriteReturningAsync(
            """
            UPDATE login_tickets SET consumed_at=@now
            WHERE ticket_digest=@digest AND consumed_at IS NULL AND expires_at>@now
                AND account_id=(SELECT account_id FROM accounts WHERE m_id=@login COLLATE BINARY)
            RETURNING account_id;
            """,
            p => { p.AddWithValue("@now", now.UtcTicks); p.AddWithValue("@digest", digest); p.AddWithValue("@login", loginId); },
            r => new Identity(new AccountId(r.GetInt32(0))), cancellationToken))?.Value;
    private sealed record Identity(AccountId Value);
}
