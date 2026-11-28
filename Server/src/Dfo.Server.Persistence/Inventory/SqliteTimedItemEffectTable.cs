using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Inventory;

internal sealed class SqliteTimedItemEffectTable(SqliteDatabase database) : ITimedItemEffectTable
{
    public ValueTask<IReadOnlyList<TimedItemEffectRow>> ReadActiveAsync(CharacterId character, DateTimeOffset at, CancellationToken ct) =>
        database.ReadAllAsync(
            """
            SELECT item_id, expires_at_utc_ticks FROM character_timed_item_effects
            WHERE character_id = @character AND expires_at_utc_ticks > @at
            ORDER BY item_id;
            """,
            parameters =>
            {
                parameters.AddWithValue("@character", character.Value);
                parameters.AddWithValue("@at", at.UtcTicks);
            }, reader => new TimedItemEffectRow(reader.GetInt32(0), new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero)), ct);

    public async ValueTask<bool> TryStartAsync(CharacterId character, TimedItemEffectRow effect, DateTimeOffset at, CancellationToken ct) =>
        await database.ExecuteAsync(
            """
            INSERT INTO character_timed_item_effects (character_id, item_id, expires_at_utc_ticks)
            VALUES (@character, @item, @expires)
            ON CONFLICT (character_id, item_id) DO UPDATE SET expires_at_utc_ticks = excluded.expires_at_utc_ticks
            WHERE character_timed_item_effects.expires_at_utc_ticks <= @at;
            """,
            parameters =>
            {
                parameters.AddWithValue("@character", character.Value);
                parameters.AddWithValue("@item", effect.ItemId);
                parameters.AddWithValue("@expires", effect.ExpiresAt.UtcTicks);
                parameters.AddWithValue("@at", at.UtcTicks);
            }, ct).ConfigureAwait(false) == 1;
}
