using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Data.Administration;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Administration;

internal sealed class SqliteGmCeraPersistence(SqliteDatabase database) : IGmCeraPersistence
{
    public async ValueTask AcquireWriteAsync(CancellationToken token) =>
        _ = await database.ExecuteAsync("UPDATE gm_cera_operations SET request_id=request_id WHERE 0;", _ => { }, token);
    public ValueTask<GmStoredPage<GmCeraAccountIdentity>> AccountsAsync(GmPlayerFilter filter, CancellationToken token) => database.ReadAsync(async (command, ct) =>
    {
        const string from = "FROM accounts WHERE (@id IS NULL OR account_id=@id) AND (@q IS NULL OR instr(m_id,@q)>0)";
        command.Parameters.AddWithValue("@id", (object?)filter.AccountId ?? DBNull.Value); command.Parameters.AddWithValue("@q", (object?)filter.Q ?? DBNull.Value);
        command.CommandText = "SELECT COUNT(*) " + from; var total = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        command.Parameters.AddWithValue("@limit", filter.PageSize); command.Parameters.AddWithValue("@offset", (long)(filter.Page - 1) * filter.PageSize);
        command.CommandText = "SELECT account_id,m_id " + from + " ORDER BY account_id LIMIT @limit OFFSET @offset;";
        var rows = new List<GmCeraAccountIdentity>(); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetInt32(0), reader.GetString(1)));
        return new GmStoredPage<GmCeraAccountIdentity>(rows, total);
    }, token);
    public ValueTask<GmCeraOperation?> OperationAsync(long administrator, Guid id, CancellationToken token) => database.ReadFirstAsync(
        "SELECT payload_hash,receipt FROM gm_cera_operations WHERE administrator_id=@admin AND request_id=@id;",
        p => { p.AddWithValue("@admin", administrator); p.AddWithValue("@id", id.ToString("N")); },
        r => new GmCeraOperation(r.GetString(0), Read(r.GetString(1))), token);
    public ValueTask<GmPage<GmCeraReceipt>> OperationsAsync(long administrator, int page, int size, CancellationToken token) => database.ReadAsync(async (command, ct) =>
    {
        command.Parameters.AddWithValue("@admin", administrator); command.CommandText = "SELECT COUNT(*) FROM gm_cera_operations WHERE administrator_id=@admin;";
        var total = Convert.ToInt64(await command.ExecuteScalarAsync(ct)); command.Parameters.AddWithValue("@limit", size); command.Parameters.AddWithValue("@offset", (long)(page - 1) * size);
        command.CommandText = "SELECT receipt FROM gm_cera_operations WHERE administrator_id=@admin ORDER BY committed_at DESC,request_id DESC LIMIT @limit OFFSET @offset;";
        var rows = new List<GmCeraReceipt>(); await using var reader = await command.ExecuteReaderAsync(ct); while (await reader.ReadAsync(ct)) rows.Add(Read(reader.GetString(0)));
        return new GmPage<GmCeraReceipt>(rows, total, page, size, DateTimeOffset.UtcNow);
    }, token);
    public async ValueTask SaveOperationAsync(long administrator, GmCeraOperation operation, CancellationToken token) =>
        _ = await database.ExecuteAsync("INSERT INTO gm_cera_operations(administrator_id,request_id,payload_hash,committed_at,receipt) VALUES(@admin,@id,@hash,@time,@receipt);",
            p => { p.AddWithValue("@admin", administrator); p.AddWithValue("@id", operation.Receipt.RequestId.ToString("N")); p.AddWithValue("@hash", operation.PayloadHash);
                p.AddWithValue("@time", operation.Receipt.CommittedAt.ToUnixTimeMilliseconds()); p.AddWithValue("@receipt", JsonSerializer.Serialize(operation.Receipt)); }, token);
    private static GmCeraReceipt Read(string value) => JsonSerializer.Deserialize<GmCeraReceipt>(value) ?? throw new InvalidDataException("Invalid cera receipt.");
}
