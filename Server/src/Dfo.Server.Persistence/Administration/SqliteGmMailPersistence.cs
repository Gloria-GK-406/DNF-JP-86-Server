using System.Text;
using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Data.Administration;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Administration;

internal sealed class SqliteGmMailPersistence(SqliteDatabase database) : IGmMailPersistence
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public async ValueTask AcquireWriteAsync(CancellationToken token) =>
        _ = await database.ExecuteAsync("UPDATE gm_mail_operations SET request_id=request_id WHERE 0;", _ => { }, token);
    public ValueTask<GmMailDocument?> DraftAsync(long administrator, CancellationToken token) =>
        database.ReadFirstAsync("SELECT document FROM gm_mail_drafts WHERE administrator_id=@admin;", p => p.AddWithValue("@admin", administrator),
            r => Read<GmMailDocument>(r.GetString(0)), token);
    public async ValueTask SaveDraftAsync(long administrator, GmMailDocument value, CancellationToken token) =>
        _ = await database.ExecuteAsync("INSERT INTO gm_mail_drafts(administrator_id,document) VALUES(@admin,@document) ON CONFLICT(administrator_id) DO UPDATE SET document=excluded.document;",
            p => { p.AddWithValue("@admin", administrator); p.AddWithValue("@document", JsonSerializer.Serialize(value)); }, token);
    public ValueTask<IReadOnlyList<GmMailDocument>> TemplatesAsync(long administrator, CancellationToken token) =>
        database.ReadAllAsync("SELECT document FROM gm_mail_templates WHERE administrator_id=@admin ORDER BY name,id;", p => p.AddWithValue("@admin", administrator), r => Read<GmMailDocument>(r.GetString(0)), token);
    public async ValueTask SaveTemplateAsync(long administrator, GmMailDocument value, CancellationToken token) =>
        _ = await database.ExecuteAsync("INSERT INTO gm_mail_templates(administrator_id,id,name,document) VALUES(@admin,@id,@name,@document) ON CONFLICT(administrator_id,id) DO UPDATE SET name=excluded.name,document=excluded.document;",
            p => { p.AddWithValue("@admin", administrator); p.AddWithValue("@id", value.Id.ToString("N")); p.AddWithValue("@name", value.Name); p.AddWithValue("@document", JsonSerializer.Serialize(value)); }, token);
    public async ValueTask DeleteTemplateAsync(long administrator, Guid id, CancellationToken token) =>
        _ = await database.ExecuteAsync("DELETE FROM gm_mail_templates WHERE administrator_id=@admin AND id=@id;", p => { p.AddWithValue("@admin", administrator); p.AddWithValue("@id", id.ToString("N")); }, token);
    public ValueTask<GmStoredPage<GmMailRecipientRecord>> RecipientsAsync(GmPlayerFilter filter, CancellationToken token) => database.ReadAsync(async (command, ct) =>
    {
        command.Connection!.CreateFunction<byte[]?, bool>("gm_valid_mail_name", ValidName, isDeterministic: true);
        const string from = "FROM characters c JOIN accounts a ON a.account_id=c.account_id WHERE c.delete_flag=0 AND (@id IS NULL OR c.character_id=@id) AND (@q IS NULL OR (gm_valid_mail_name(c.name_bytes)=1 AND instr(CAST(c.name_bytes AS TEXT),@q)>0))";
        command.Parameters.AddWithValue("@id", (object?)filter.CharacterId ?? DBNull.Value); command.Parameters.AddWithValue("@q", (object?)filter.Q ?? DBNull.Value);
        command.CommandText = "SELECT COUNT(*) " + from; var total = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        command.Parameters.AddWithValue("@limit", filter.PageSize); command.Parameters.AddWithValue("@offset", (long)(filter.Page - 1) * filter.PageSize);
        command.CommandText = "SELECT c.character_id,c.account_id,c.name_bytes,c.level,c.job,c.grow_type " + from + " ORDER BY c.character_id LIMIT @limit OFFSET @offset;";
        var values = new List<GmMailRecipientRecord>(); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) values.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : (byte[])reader.GetValue(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)));
        return new GmStoredPage<GmMailRecipientRecord>(values, total);
    }, token);
    public ValueTask<GmMailOperation?> OperationAsync(long administrator, Guid id, CancellationToken token) =>
        database.ReadFirstAsync("SELECT payload_hash,receipt FROM gm_mail_operations WHERE administrator_id=@admin AND request_id=@id;",
            p => { p.AddWithValue("@admin", administrator); p.AddWithValue("@id", id.ToString("N")); }, r => new GmMailOperation(r.GetString(0), Read<GmMailReceipt>(r.GetString(1))), token);
    public ValueTask<GmPage<GmMailReceipt>> OperationsAsync(long administrator, int page, int size, CancellationToken token) => database.ReadAsync(async (command, ct) =>
    {
        command.Parameters.AddWithValue("@admin", administrator); command.CommandText = "SELECT COUNT(*) FROM gm_mail_operations WHERE administrator_id=@admin;";
        var total = Convert.ToInt64(await command.ExecuteScalarAsync(ct)); command.Parameters.AddWithValue("@limit", size); command.Parameters.AddWithValue("@offset", (long)(page - 1) * size);
        command.CommandText = "SELECT receipt FROM gm_mail_operations WHERE administrator_id=@admin ORDER BY committed_at DESC,request_id DESC LIMIT @limit OFFSET @offset;";
        var values = new List<GmMailReceipt>(); await using var reader = await command.ExecuteReaderAsync(ct); while (await reader.ReadAsync(ct)) values.Add(Read<GmMailReceipt>(reader.GetString(0)));
        return new GmPage<GmMailReceipt>(values, total, page, size, DateTimeOffset.UtcNow);
    }, token);
    public async ValueTask SaveOperationAsync(long administrator, GmMailOperation operation, CancellationToken token) =>
        _ = await database.ExecuteAsync("INSERT INTO gm_mail_operations(administrator_id,request_id,payload_hash,committed_at,receipt) VALUES(@admin,@id,@hash,@time,@receipt);",
            p => { p.AddWithValue("@admin", administrator); p.AddWithValue("@id", operation.Receipt.RequestId.ToString("N")); p.AddWithValue("@hash", operation.PayloadHash); p.AddWithValue("@time", operation.Receipt.CommittedAt.ToUnixTimeMilliseconds()); p.AddWithValue("@receipt", JsonSerializer.Serialize(operation.Receipt)); }, token);
    public async ValueTask AssertMailWireRangeAsync(long mailId, CancellationToken token)
    {
        if (mailId is <= 0 or > int.MaxValue) throw new InvalidDataException("Mail ID exceeds the existing wire range.");
        var bad = await database.ReadFirstAsync("SELECT attachment_id FROM mailbox_attachments WHERE message_id=@id AND (attachment_id<=0 OR attachment_id>=1073741824) LIMIT 1;",
            p => p.AddWithValue("@id", mailId), r => new InvalidAttachment(r.GetInt64(0)), token);
        if (bad is not null) throw new InvalidDataException("Attachment ID exceeds the existing wire range.");
    }
    private sealed record InvalidAttachment(long Id);
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException("Invalid GM mail document.");
    private static bool ValidName(byte[]? bytes) { if (bytes is null) return false; try { _ = Utf8.GetString(bytes); return true; } catch (DecoderFallbackException) { return false; } }
}
