using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Inventory;

internal sealed class SqliteConsumableCooldownTable(SqliteDatabase database) : IConsumableCooldownTable
{
    public async ValueTask<bool> IsActiveAsync(CharacterId character, ItemCooldownKey key, DateTimeOffset at, CancellationToken ct) =>
        (await database.ReadAllAsync(
            """
            SELECT 1 FROM character_consumable_cooldowns
            WHERE character_id = @character AND is_shared = @shared AND cooldown_key = @key AND expires_at_utc_ticks > @at;
            """,
            parameters =>
            {
                parameters.AddWithValue("@character", character.Value);
                parameters.AddWithValue("@shared", key.IsShared);
                parameters.AddWithValue("@key", key.Value);
                parameters.AddWithValue("@at", at.UtcTicks);
            }, reader => reader.GetInt32(0), ct).ConfigureAwait(false)).Count != 0;

    public ValueTask<IReadOnlyList<ConsumableCooldownRow>> ReadActiveAsync(CharacterId character, DateTimeOffset at, CancellationToken ct) =>
        database.ReadAllAsync(
            """
            SELECT is_shared, cooldown_key, item_id, expires_at_utc_ticks FROM character_consumable_cooldowns
            WHERE character_id = @character AND expires_at_utc_ticks > @at;
            """,
            parameters =>
            {
                parameters.AddWithValue("@character", character.Value);
                parameters.AddWithValue("@at", at.UtcTicks);
            }, reader => new ConsumableCooldownRow(new(reader.GetBoolean(0), reader.GetInt32(1)),
                reader.GetInt32(2), new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero)), ct);

    public async ValueTask<bool> TryStartAsync(CharacterId character, ConsumableCooldownRow cooldown, DateTimeOffset at,
        CancellationToken ct) =>
        await database.ExecuteAsync(
            """
            INSERT INTO character_consumable_cooldowns (character_id, is_shared, cooldown_key, item_id, expires_at_utc_ticks)
            VALUES (@character, @shared, @key, @item, @expires)
            ON CONFLICT (character_id, is_shared, cooldown_key) DO UPDATE
            SET item_id = excluded.item_id, expires_at_utc_ticks = excluded.expires_at_utc_ticks
            WHERE character_consumable_cooldowns.expires_at_utc_ticks <= @at;
            """,
            parameters =>
            {
                parameters.AddWithValue("@character", character.Value);
                parameters.AddWithValue("@shared", cooldown.Key.IsShared);
                parameters.AddWithValue("@key", cooldown.Key.Value);
                parameters.AddWithValue("@item", cooldown.ItemId);
                parameters.AddWithValue("@expires", cooldown.ExpiresAt.UtcTicks);
                parameters.AddWithValue("@at", at.UtcTicks);
            }, ct).ConfigureAwait(false) == 1;
}
