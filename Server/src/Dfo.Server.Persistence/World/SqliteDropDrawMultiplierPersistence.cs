using Dfo.Server.Abstractions.World;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.World;

internal sealed class SqliteDropDrawMultiplierPersistence(SqliteDatabase database) : IDropDrawMultiplierPersistence
{
    public async ValueTask<IReadOnlyList<(string Key, int Multiplier)>> ReadAllAsync(CancellationToken cancellationToken) =>
        await database.ReadAllAsync("SELECT entry_key,multiplier FROM drop_draw_multiplier;", _ => { },
            reader => (reader.GetString(0), reader.GetInt32(1)), cancellationToken).ConfigureAwait(false);

    public async ValueTask SaveAsync(string key, int multiplier, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO drop_draw_multiplier(entry_key,multiplier) VALUES(@key,@multiplier)
            ON CONFLICT(entry_key) DO UPDATE SET multiplier=excluded.multiplier;
            """, parameters =>
            {
                parameters.AddWithValue("@key", key);
                parameters.AddWithValue("@multiplier", multiplier);
            }, cancellationToken).ConfigureAwait(false);
}
