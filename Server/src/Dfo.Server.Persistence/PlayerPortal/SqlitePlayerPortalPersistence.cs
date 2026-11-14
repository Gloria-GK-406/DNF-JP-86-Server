using System.Globalization;
using Dfo.Server.Abstractions.PlayerPortal;
using Dfo.Server.Data.PlayerPortal;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.PlayerPortal;

/// <remarks>
/// Settings that were never saved have no row and read as <see cref="PlayerPortalSettings.Default"/>, so a new database
/// and a migrated one are the same. Only fixed SQL fragments are concatenated; values always travel as parameters.
/// </remarks>
internal sealed class SqlitePlayerPortalPersistence(SqliteDatabase database) : IPlayerPortalPersistence
{
    private const string OperationColumns = "account_id,request_id,kind,currency,amount,payload_hash,receipt_json,period_day,committed_at";

    public async ValueTask AcquireWriteAsync(CancellationToken token) =>
        _ = await database.ExecuteAsync("UPDATE player_portal_settings SET revision=revision WHERE 0;", _ => { }, token);

    public async ValueTask<PlayerPortalSettings> ReadSettingsAsync(CancellationToken token) =>
        await database.ReadFirstAsync(
            "SELECT enabled,cera_enabled,mail_enabled,cera_per_grant,cera_per_day,token_cera_per_grant,token_cera_per_day,mail_gold_per_mail,mails_per_day,revision FROM player_portal_settings WHERE id=1;",
            _ => { },
            r => new PlayerPortalSettings(r.GetBoolean(0), r.GetBoolean(1), r.GetBoolean(2),
                new PlayerPortalLimits(r.GetInt64(3), r.GetInt64(4), r.GetInt64(5), r.GetInt64(6), r.GetInt64(7), r.GetInt64(8)), r.GetInt64(9)),
            token) ?? PlayerPortalSettings.Default;

    public async ValueTask<bool> SaveSettingsAsync(PlayerPortalSettings settings, long expectedRevision, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return await database.ExecuteAsync("""
            INSERT INTO player_portal_settings(id,enabled,cera_enabled,mail_enabled,cera_per_grant,cera_per_day,token_cera_per_grant,token_cera_per_day,mail_gold_per_mail,mails_per_day,revision)
            SELECT 1,@enabled,@cera,@mail,@ceraGrant,@ceraDay,@tokenGrant,@tokenDay,@mailGold,@mailDay,1 WHERE @revision=0 ON CONFLICT(id) DO NOTHING;
            UPDATE player_portal_settings SET enabled=@enabled,cera_enabled=@cera,mail_enabled=@mail,cera_per_grant=@ceraGrant,cera_per_day=@ceraDay,
                token_cera_per_grant=@tokenGrant,token_cera_per_day=@tokenDay,mail_gold_per_mail=@mailGold,mails_per_day=@mailDay,revision=revision+1
            WHERE id=1 AND revision=@revision AND @revision>0;
            """, p =>
        {
            var limits = settings.Limits;
            p.AddWithValue("@enabled", settings.Enabled); p.AddWithValue("@cera", settings.CeraEnabled); p.AddWithValue("@mail", settings.MailEnabled);
            p.AddWithValue("@ceraGrant", limits.CeraPerGrant); p.AddWithValue("@ceraDay", limits.CeraPerDay);
            p.AddWithValue("@tokenGrant", limits.TokenCeraPerGrant); p.AddWithValue("@tokenDay", limits.TokenCeraPerDay);
            p.AddWithValue("@mailGold", limits.MailGoldPerMail); p.AddWithValue("@mailDay", limits.MailsPerDay);
            p.AddWithValue("@revision", expectedRevision);
        }, token) == 1;
    }

    public async ValueTask SaveSessionAsync(PlayerPortalSession session, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        _ = await database.ExecuteAsync("INSERT INTO player_portal_sessions(token_hash,account_id,created_at,expires_at) VALUES(@hash,@account,@created,@expires);",
            p =>
            {
                p.AddWithValue("@hash", session.TokenHash); p.AddWithValue("@account", session.AccountId);
                p.AddWithValue("@created", session.CreatedAt.ToUnixTimeMilliseconds()); p.AddWithValue("@expires", session.ExpiresAt.ToUnixTimeMilliseconds());
            }, token);
    }

    public ValueTask<PlayerPortalSession?> FindSessionAsync(string tokenHash, CancellationToken token) =>
        database.ReadFirstAsync("SELECT token_hash,account_id,created_at,expires_at FROM player_portal_sessions WHERE token_hash=@hash;",
            p => p.AddWithValue("@hash", tokenHash),
            r => new PlayerPortalSession(r.GetString(0), r.GetInt32(1), Time(r.GetInt64(2)), Time(r.GetInt64(3))), token);

    public async ValueTask<bool> DeleteSessionAsync(string tokenHash, CancellationToken token) =>
        await database.ExecuteAsync("DELETE FROM player_portal_sessions WHERE token_hash=@hash;", p => p.AddWithValue("@hash", tokenHash), token) > 0;

    public ValueTask<int> DeleteAccountSessionsAsync(int accountId, CancellationToken token) =>
        database.ExecuteAsync("DELETE FROM player_portal_sessions WHERE account_id=@account;", p => p.AddWithValue("@account", accountId), token);

    public ValueTask<int> DeleteAllSessionsAsync(CancellationToken token) =>
        database.ExecuteAsync("DELETE FROM player_portal_sessions;", _ => { }, token);

    public ValueTask<int> DeleteExpiredSessionsAsync(DateTimeOffset now, CancellationToken token) =>
        database.ExecuteAsync("DELETE FROM player_portal_sessions WHERE expires_at<=@now;", p => p.AddWithValue("@now", now.ToUnixTimeMilliseconds()), token);

    public ValueTask<IReadOnlyList<PlayerPortalSessionEntry>> ListActiveSessionsAsync(DateTimeOffset now, CancellationToken token) =>
        database.ReadAllAsync("""
            SELECT s.account_id,IFNULL(a.m_id,''),s.created_at,s.expires_at FROM player_portal_sessions s LEFT JOIN accounts a ON a.account_id=s.account_id
            WHERE s.expires_at>@now ORDER BY s.created_at DESC,s.account_id;
            """, p => p.AddWithValue("@now", now.ToUnixTimeMilliseconds()),
            r => new PlayerPortalSessionEntry(r.GetInt32(0), r.GetString(1), Time(r.GetInt64(2)), Time(r.GetInt64(3))), token);

    public ValueTask<PlayerPortalOperation?> FindOperationAsync(int accountId, Guid requestId, CancellationToken token) =>
        database.ReadFirstAsync($"SELECT {OperationColumns} FROM player_portal_operations WHERE account_id=@account AND request_id=@id;",
            p => { p.AddWithValue("@account", accountId); p.AddWithValue("@id", requestId.ToString("N")); }, ReadOperation, token);

    public async ValueTask SaveOperationAsync(PlayerPortalOperation operation, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _ = await database.ExecuteAsync($"INSERT INTO player_portal_operations({OperationColumns}) VALUES(@account,@id,@kind,@currency,@amount,@hash,@receipt,@day,@time);",
            p =>
            {
                p.AddWithValue("@account", operation.AccountId); p.AddWithValue("@id", operation.RequestId.ToString("N"));
                p.AddWithValue("@kind", KindText(operation.Kind)); p.AddWithValue("@currency", (object?)operation.Currency ?? DBNull.Value);
                p.AddWithValue("@amount", operation.Amount); p.AddWithValue("@hash", operation.PayloadHash); p.AddWithValue("@receipt", operation.ReceiptJson);
                p.AddWithValue("@day", DayText(operation.PeriodDay)); p.AddWithValue("@time", operation.CommittedAt.ToUnixTimeMilliseconds());
            }, token);
    }

    public ValueTask<PlayerPortalUsage> UsageAsync(int accountId, PlayerPortalOperationKind kind, string? currency, DateOnly periodDay, CancellationToken token) =>
        database.ReadAsync(async (command, ct) =>
        {
            command.CommandText = """
                SELECT COUNT(*),COALESCE(SUM(amount),0) FROM player_portal_operations
                WHERE account_id=@account AND kind=@kind AND period_day=@day AND (@currency IS NULL OR currency=@currency);
                """;
            command.Parameters.AddWithValue("@account", accountId); command.Parameters.AddWithValue("@kind", KindText(kind));
            command.Parameters.AddWithValue("@day", DayText(periodDay)); command.Parameters.AddWithValue("@currency", (object?)currency ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            _ = await reader.ReadAsync(ct);
            return new PlayerPortalUsage(reader.GetInt64(0), reader.GetInt64(1));
        }, token);

    public ValueTask<PlayerPortalOperationPage> OperationsAsync(int accountId, PlayerPortalOperationKind? kind, int page, int pageSize, CancellationToken token) =>
        database.ReadAsync(async (command, ct) =>
        {
            var where = " WHERE account_id=@account";
            command.Parameters.AddWithValue("@account", accountId);
            if (kind is { } only) { where += " AND kind=@kind"; command.Parameters.AddWithValue("@kind", KindText(only)); }
            command.CommandText = "SELECT COUNT(*) FROM player_portal_operations" + where + ";";
            var total = Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            command.Parameters.AddWithValue("@limit", pageSize); command.Parameters.AddWithValue("@offset", (long)(page - 1) * pageSize);
            command.CommandText = $"SELECT {OperationColumns} FROM player_portal_operations" + where + " ORDER BY committed_at DESC,request_id DESC LIMIT @limit OFFSET @offset;";
            var rows = new List<PlayerPortalOperation>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) rows.Add(ReadOperation(reader));
            return new PlayerPortalOperationPage(rows, total, page, pageSize);
        }, token);

    private static PlayerPortalOperation ReadOperation(SqliteDataReader r) =>
        new(r.GetInt32(0), Guid.ParseExact(r.GetString(1), "N"), Kind(r.GetString(2)), r.IsDBNull(3) ? null : r.GetString(3), r.GetInt64(4),
            r.GetString(5), r.GetString(6), DateOnly.ParseExact(r.GetString(7), "yyyy-MM-dd", CultureInfo.InvariantCulture), Time(r.GetInt64(8)));

    private static string KindText(PlayerPortalOperationKind kind) => kind switch
    {
        PlayerPortalOperationKind.Cera => "cera",
        PlayerPortalOperationKind.Mail => "mail",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown player portal operation kind."),
    };

    private static PlayerPortalOperationKind Kind(string text) => text switch
    {
        "cera" => PlayerPortalOperationKind.Cera,
        "mail" => PlayerPortalOperationKind.Mail,
        _ => throw new InvalidDataException($"Unknown player portal operation kind '{text}'."),
    };

    private static string DayText(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateTimeOffset Time(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
}
