using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
namespace Dfo.Server.Persistence.Accounts;

internal sealed class SqlitePremiumServiceUsageTable(SqliteDatabase database) : IPremiumServiceUsageTable
{
    public async ValueTask<int> ReadAsync(CharacterId id, string key, string period, CancellationToken token) =>
        (await database.ReadAllAsync("SELECT value FROM character_daily_counters WHERE character_id=@id AND counter_key=@key AND period=@period",
            p => { p.AddWithValue("@id", id.Value); p.AddWithValue("@key", key); p.AddWithValue("@period", period); },
            r => r.GetInt32(0), token).ConfigureAwait(false)).SingleOrDefault();

    public ValueTask<int> TryIncrementAsync(CharacterId id, string key, string period, int limit, CancellationToken token) =>
        database.ExecuteAsync("""
            INSERT INTO character_daily_counters(character_id,counter_key,period,value) VALUES(@id,@key,@period,1)
            ON CONFLICT(character_id,counter_key) DO UPDATE SET period=excluded.period,
                value=CASE WHEN period=excluded.period THEN value+1 ELSE 1 END
            WHERE period IS NULL OR period<>excluded.period OR (value>=0 AND value<@limit)
            """, p => { p.AddWithValue("@id", id.Value); p.AddWithValue("@key", key);
                p.AddWithValue("@period", period); p.AddWithValue("@limit", limit); }, token);
}
