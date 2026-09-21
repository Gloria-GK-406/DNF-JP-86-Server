using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
namespace Dfo.Server.Persistence.Accounts;

internal sealed class SqlitePremiumServiceUsageTable(SqliteDatabase database) : IPremiumServiceUsageTable
{
    public async ValueTask<PremiumServiceUsageRow?> ReadAsync(CharacterId id, string key, CancellationToken token) =>
        (await database.ReadAllAsync("SELECT value,last_consumed_at FROM character_daily_counters WHERE character_id=@id AND counter_key=@key",
            p => { p.AddWithValue("@id", id.Value); p.AddWithValue("@key", key); },
            r => new PremiumServiceUsageRow(r.GetInt32(0), r.IsDBNull(1) ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(1))),
            token).ConfigureAwait(false)).SingleOrDefault();

    public ValueTask<int> TryWriteAsync(CharacterId id, string key, PremiumServiceUsageRow? expected,
        int usedCount, DateTimeOffset consumedAt, CancellationToken token) =>
        database.ExecuteAsync("""
            INSERT INTO character_daily_counters(character_id,counter_key,period,value,last_consumed_at)
                VALUES(@id,@key,NULL,@used,@time)
            ON CONFLICT(character_id,counter_key) DO UPDATE SET period=NULL,value=excluded.value,last_consumed_at=excluded.last_consumed_at
            WHERE @exists=1 AND value=@oldUsed AND last_consumed_at IS @oldTime
            """, p => { p.AddWithValue("@id", id.Value); p.AddWithValue("@key", key);
                p.AddWithValue("@used", usedCount); p.AddWithValue("@time", consumedAt.ToUnixTimeMilliseconds());
                p.AddWithValue("@exists", expected is not null ? 1 : 0);
                p.AddWithValue("@oldUsed", expected?.UsedCount ?? 0);
                p.AddWithValue("@oldTime", (object?)expected?.LastConsumedAt?.ToUnixTimeMilliseconds() ?? DBNull.Value); }, token);
}
