using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Initialization;
using Dfo.Server.Persistence.Database;
namespace Dfo.Server.Persistence.Inventory;

internal sealed class SqliteDailyRefillSettingsPersistence(SqliteDatabase database) : IDailyRefillSettingsPersistence
{
    public async ValueTask AcquireWriteAsync(CancellationToken ct) =>
        _ = await database.ExecuteAsync("UPDATE daily_refill_settings SET revision=revision WHERE 0;", _ => { }, ct);
    public ValueTask<InitializationStoredDocument?> ReadAsync(CancellationToken ct) => database.ReadFirstAsync(
        "SELECT revision,input,updated_at,updated_by FROM daily_refill_settings WHERE id=1;", _ => { },
        r => new InitializationStoredDocument(r.GetInt64(0), r.GetString(1), DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(2)), r.GetString(3)), ct);
    public async ValueTask<bool> SaveAsync(long revision, string input, DateTimeOffset at, string actor, CancellationToken ct) =>
        await database.ExecuteAsync("""
            INSERT INTO daily_refill_settings(id,revision,input,updated_at,updated_by)
            SELECT 1,1,@input,@at,@actor WHERE @revision=0 ON CONFLICT(id) DO NOTHING;
            UPDATE daily_refill_settings SET revision=revision+1,input=@input,updated_at=@at,updated_by=@actor
            WHERE id=1 AND revision=@revision AND @revision>0;
            """, p =>
        {
            p.AddWithValue("@revision", revision); p.AddWithValue("@input", input);
            p.AddWithValue("@at", at.ToUnixTimeMilliseconds()); p.AddWithValue("@actor", actor);
        }, ct) == 1;
}
