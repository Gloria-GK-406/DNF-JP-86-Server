using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Data.Administration;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Administration;

internal sealed class SqliteGmAdministrationPersistence(SqliteDatabase database) : IGmAdministrationPersistence
{
    public async ValueTask AcquireWriteIntentAsync(CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync("UPDATE gm_administrators SET enabled=enabled WHERE 0;", _ => { }, cancellationToken);
    public async ValueTask<bool> HasAdministratorAsync(CancellationToken cancellationToken) =>
        await database.ReadFirstAsync("SELECT id,login_name,password_record,enabled,permissions FROM gm_administrators LIMIT 1;", _ => { }, ReadAdministrator, cancellationToken) is not null;
    public ValueTask<GmAdministrator?> FindAdministratorAsync(string loginName, CancellationToken cancellationToken) =>
        database.ReadFirstAsync("SELECT id,login_name,password_record,enabled,permissions FROM gm_administrators WHERE login_name=@name;",
            p => p.AddWithValue("@name", loginName), ReadAdministrator, cancellationToken);
    public ValueTask<GmAdministrator?> FindAdministratorAsync(long id, CancellationToken cancellationToken) =>
        database.ReadFirstAsync("SELECT id,login_name,password_record,enabled,permissions FROM gm_administrators WHERE id=@id;",
            p => p.AddWithValue("@id", id), ReadAdministrator, cancellationToken);
    public async ValueTask CreateAdministratorAsync(string loginName, string passwordRecord, GmPermission permissions, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync("INSERT INTO gm_administrators(login_name,password_record,enabled,permissions) VALUES(@name,@password,1,@permissions);",
            p => { p.AddWithValue("@name", loginName); p.AddWithValue("@password", passwordRecord); p.AddWithValue("@permissions", (int)permissions); }, cancellationToken);
    public async ValueTask SaveSessionAsync(GmSession session, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync("INSERT INTO gm_sessions(token_hash,administrator_id,expires_at) VALUES(@hash,@admin,@expires);",
            p => { p.AddWithValue("@hash", session.TokenHash); p.AddWithValue("@admin", session.AdministratorId); p.AddWithValue("@expires", session.ExpiresAt.ToUnixTimeMilliseconds()); }, cancellationToken);
    public ValueTask<GmSession?> FindSessionAsync(string hash, CancellationToken cancellationToken) =>
        database.ReadFirstAsync("SELECT token_hash,administrator_id,expires_at FROM gm_sessions WHERE token_hash=@hash;",
            p => p.AddWithValue("@hash", hash), r => new GmSession(r.GetString(0), r.GetInt64(1), DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(2))), cancellationToken);
    public async ValueTask DeleteSessionAsync(string hash, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync("DELETE FROM gm_sessions WHERE token_hash=@hash;", p => p.AddWithValue("@hash", hash), cancellationToken);
    public async ValueTask AppendAuditAsync(GmAuditRecord record, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync("INSERT INTO gm_audit(operation_id,occurred_at,action,administrator_id,administrator_name,target,outcome,reason) VALUES(@operation,@time,@action,@admin,@name,@target,@outcome,@reason);",
            p =>
            {
                p.AddWithValue("@operation", record.OperationId); p.AddWithValue("@time", record.OccurredAt.ToUnixTimeMilliseconds());
                p.AddWithValue("@action", record.Action); p.AddWithValue("@admin", (object?)record.AdministratorId ?? DBNull.Value);
                p.AddWithValue("@name", (object?)record.AdministratorName ?? DBNull.Value); p.AddWithValue("@target", record.Target);
                p.AddWithValue("@outcome", record.Outcome); p.AddWithValue("@reason", record.Reason);
            }, cancellationToken);
    public ValueTask<GmAuditPage> QueryAuditAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        database.ReadAsync(async (command, token) =>
        {
            command.CommandText = "SELECT COUNT(*) FROM gm_audit;";
            var total = Convert.ToInt64(await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT operation_id,occurred_at,action,administrator_id,administrator_name,target,outcome,reason FROM gm_audit ORDER BY occurred_at DESC,operation_id DESC LIMIT @limit OFFSET @offset;";
            command.Parameters.AddWithValue("@limit", pageSize); command.Parameters.AddWithValue("@offset", (long)(page - 1) * pageSize);
            var records = new List<GmAuditRecord>();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) records.Add(new(reader.GetString(0), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
            return new GmAuditPage(records, total, page, pageSize);
        }, cancellationToken);
    private static GmAdministrator ReadAdministrator(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), (GmPermission)reader.GetInt32(4));
}
