using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Guilds;

internal sealed class SqliteGuildWarehouseStore(SqliteDatabase database) : IGuildWarehouseStore
{
    public ValueTask<IReadOnlyList<GuildWarehouseHistoryEntry>> ReadWarehouseHistoryAsync(GuildId guild, int limit,
        CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT actor_name,action,item_id,item_count,occurred_at
            FROM guild_warehouse_history WHERE guild_id=@guild ORDER BY id DESC LIMIT @limit
            """, parameters =>
            {
                BindGuild(parameters, guild);
                parameters.AddWithValue("@limit", limit);
            }, r => new GuildWarehouseHistoryEntry((byte[])r.GetValue(0), (GuildWarehouseAction)r.GetInt32(1),
                r.GetInt32(2), r.GetInt32(3), DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(4))), ct);

    public async ValueTask AppendWarehouseHistoryAsync(GuildId guild, CharacterId actor, GuildWarehouseAction action,
        int itemId, int count, DateTimeOffset at, CancellationToken ct)
    {
        var inserted = await database.ExecuteAsync("""
            INSERT INTO guild_warehouse_history(guild_id,actor_name,action,item_id,item_count,occurred_at)
            SELECT @guild,COALESCE(c.name_bytes, CAST(c.name AS BLOB)),@action,@item,@count,@at
            FROM characters c JOIN guild_members m USING(character_id)
            WHERE c.character_id=@character AND m.guild_id=@guild
            """, parameters =>
            {
                BindGuild(parameters, guild);
                BindCharacter(parameters, actor);
                parameters.AddWithValue("@action", (int)action);
                parameters.AddWithValue("@item", itemId);
                parameters.AddWithValue("@count", count);
                parameters.AddWithValue("@at", at.ToUnixTimeSeconds());
            }, ct).ConfigureAwait(false);
        if (inserted != 1)
            throw new InvalidOperationException("The guild warehouse operation lost its current member.");
    }

    public ValueTask<int> ReadWarehouseCapacityAsync(GuildId guild, CancellationToken ct) =>
        database.ReadAsync(async (command, token) =>
        {
            command.CommandText = "SELECT capacity FROM guild_warehouse WHERE guild_id=@guild";
            BindGuild(command.Parameters, guild);
            return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is long capacity ? checked((int)capacity) : 0;
        }, ct);

    public ValueTask<IReadOnlyList<GuildWarehouseItem>> ReadWarehouseItemsAsync(GuildId guild, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT w.slot_index,w.kind,w.item_uid,w.item_count,
                   COALESCE(w.item_core,i.item_core),i.appearance_item_id
            FROM guild_warehouse_items w LEFT JOIN items i ON i.item_uid=w.item_uid
            WHERE w.guild_id=@guild ORDER BY w.slot_index
            """, p => BindGuild(p, guild), r =>
        {
            var pile = r.GetString(1) == "stackable";
            var core = InventoryItemCore.Decode((byte[])r.GetValue(4));
            var item = ItemSnapshot.FromCore(core, pile, pile ? null : new ItemIdentity(r.GetInt32(2))) with
            {
                Count = r.GetInt32(3),
                AppearanceItemId = r.IsDBNull(5) ? null : r.GetInt32(5),
            };
            return new GuildWarehouseItem(r.GetInt32(0), item);
        }, ct);

    public ValueTask<bool> HasWarehouseItemsAsync(GuildId guild, CancellationToken ct) =>
        database.ReadAsync(async (command, token) =>
        {
            command.CommandText = "SELECT 1 FROM guild_warehouse_items WHERE guild_id=@guild LIMIT 1";
            BindGuild(command.Parameters, guild);
            return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not null;
        }, ct);

    public async ValueTask SetWarehouseCapacityAsync(GuildId guild, int capacity, CancellationToken ct)
    {
        var changed = await database.ExecuteAsync("""
            INSERT INTO guild_warehouse(guild_id,capacity) VALUES(@guild,@capacity)
            ON CONFLICT(guild_id) DO UPDATE SET capacity=excluded.capacity
            """, p =>
            {
                BindGuild(p, guild);
                p.AddWithValue("@capacity", capacity);
            }, ct).ConfigureAwait(false);
        if (changed != 1)
            throw new InvalidOperationException("Guild warehouse expansion did not persist.");
    }

    private static void BindGuild(SqliteParameterCollection parameters, GuildId guild) =>
        parameters.AddWithValue("@guild", (long)guild.Value);

    private static void BindCharacter(SqliteParameterCollection parameters, CharacterId character) =>
        parameters.AddWithValue("@character", character.Value);
}
