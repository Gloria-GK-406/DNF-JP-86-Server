using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
namespace Dfo.Server.Persistence.Accounts;

internal sealed class SqliteCharacterFatigueTable(SqliteDatabase database) : ICharacterFatigueTable
{
    public async ValueTask<CharacterFatigueRow?> ReadAsync(CharacterId id, CancellationToken token) =>
        (await database.ReadAllAsync("SELECT used,consumed_today,updated_at FROM character_fatigue WHERE character_id=@id",
            p => p.AddWithValue("@id", id.Value),
            r => new CharacterFatigueRow(r.GetInt32(0), r.GetInt32(1), DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(2))), token)
            .ConfigureAwait(false)).SingleOrDefault();
    public ValueTask<int> TryWriteAsync(CharacterId id, CharacterFatigueRow? expected, CharacterFatigueRow value, CancellationToken token) =>
        database.ExecuteAsync("""
            INSERT INTO character_fatigue(character_id,used,consumed_today,updated_at) VALUES(@id,@used,@consumed,@time)
            ON CONFLICT(character_id) DO UPDATE SET used=excluded.used,consumed_today=excluded.consumed_today,updated_at=excluded.updated_at
            WHERE @exists=1 AND used=@oldUsed AND consumed_today=@oldConsumed AND updated_at=@oldTime
            """, p => {
                p.AddWithValue("@id", id.Value); p.AddWithValue("@used", value.Used);
                p.AddWithValue("@consumed", value.ConsumedToday); p.AddWithValue("@time", value.UpdatedAt.ToUnixTimeMilliseconds());
                p.AddWithValue("@exists", expected is null ? 0 : 1); p.AddWithValue("@oldUsed", expected?.Used ?? 0);
                p.AddWithValue("@oldConsumed", expected?.ConsumedToday ?? 0); p.AddWithValue("@oldTime", expected?.UpdatedAt.ToUnixTimeMilliseconds() ?? 0);
            }, token);
}
