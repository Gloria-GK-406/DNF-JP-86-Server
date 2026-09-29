using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Accounts;

internal sealed class SqliteConsumableUsageTable(SqliteDatabase database) : IConsumableUsageTable
{
    private static string Key(int itemId) => "consumable_used_" + itemId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public async ValueTask<ConsumableUsageRow?> ReadAsync(CharacterId id, int itemId, CancellationToken token) =>
        (await database.ReadAllAsync("SELECT value,last_consumed_at FROM character_daily_counters WHERE character_id=@id AND counter_key=@key",
            p => { p.AddWithValue("@id", id.Value); p.AddWithValue("@key", Key(itemId)); },
            r => new ConsumableUsageRow(r.GetInt32(0), r.IsDBNull(1) ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(1))),
            token).ConfigureAwait(false)).SingleOrDefault();

    public async ValueTask<IReadOnlyDictionary<int, ConsumableUsageRow>> ReadAllAsync(CharacterId id, CancellationToken token)
    {
        var rows = await database.ReadAllAsync(
            "SELECT counter_key,value,last_consumed_at FROM character_daily_counters WHERE character_id=@id AND counter_key GLOB 'consumable_used_*'",
            p => p.AddWithValue("@id", id.Value),
            r => (Key: r.GetString(0), Row: new ConsumableUsageRow(r.GetInt32(1),
                r.IsDBNull(2) ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(2)))), token).ConfigureAwait(false);
        return rows.ToDictionary(x => int.Parse(x.Key["consumable_used_".Length..],
            System.Globalization.CultureInfo.InvariantCulture), x => x.Row);
    }

    public ValueTask<int> TryWriteAsync(CharacterId id, int itemId, ConsumableUsageRow? expected,
        int usedCount, DateTimeOffset usedAt, CancellationToken token) =>
        database.ExecuteAsync("""
            INSERT INTO character_daily_counters(character_id,counter_key,period,value,last_consumed_at)
                VALUES(@id,@key,NULL,@used,@time)
            ON CONFLICT(character_id,counter_key) DO UPDATE SET period=NULL,value=excluded.value,last_consumed_at=excluded.last_consumed_at
            WHERE @exists=1 AND value=@oldUsed AND last_consumed_at IS @oldTime
            """, p => { p.AddWithValue("@id", id.Value); p.AddWithValue("@key", Key(itemId));
                p.AddWithValue("@used", usedCount); p.AddWithValue("@time", usedAt.ToUnixTimeMilliseconds());
                p.AddWithValue("@exists", expected is not null ? 1 : 0);
                p.AddWithValue("@oldUsed", expected?.UsedCount ?? 0);
                p.AddWithValue("@oldTime", (object?)expected?.LastUsedAt?.ToUnixTimeMilliseconds() ?? DBNull.Value); }, token);
}
