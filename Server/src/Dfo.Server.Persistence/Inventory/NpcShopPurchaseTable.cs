using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

internal sealed class SqliteNpcShopPurchaseTable(SqliteDatabase database) : INpcShopPurchaseTable
{
    public async ValueTask<int> ReadAsync(NpcShopPurchaseKey key, CancellationToken cancellationToken)
    {
        var rows = await database.ReadAllAsync(
            """
            SELECT count FROM npc_shop_purchase_counts
            WHERE owner_scope = @scope AND owner_id = @owner AND npc_id = @npc
                AND item_id = @item AND period = @period;
            """,
            parameters => Bind(parameters, key), reader => reader.GetInt32(0), cancellationToken)
            .ConfigureAwait(false);
        return rows.Count == 0 ? 0 : rows[0];
    }

    public async ValueTask<bool> TryAddAsync(
        NpcShopPurchaseKey key, int count, int limit, CancellationToken cancellationToken)
    {
        if (count <= 0 || limit <= 0 || count > limit)
            return false;
        var changed = false;
        await database.WriteAsync(async (command, ct) =>
        {
            command.CommandText = """
                INSERT INTO npc_shop_purchase_counts(owner_scope, owner_id, npc_id, item_id, period, count)
                VALUES (@scope, @owner, @npc, @item, @period, @count)
                ON CONFLICT(owner_scope, owner_id, npc_id, item_id, period) DO UPDATE SET
                    count = count + excluded.count
                WHERE count <= @limit - excluded.count;
                """;
            Bind(command.Parameters, key);
            command.Parameters.AddWithValue("@count", count);
            command.Parameters.AddWithValue("@limit", limit);
            changed = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
        }, cancellationToken).ConfigureAwait(false);
        return changed;
    }

    private static void Bind(SqliteParameterCollection parameters, NpcShopPurchaseKey key)
    {
        parameters.AddWithValue("@scope", key.OwnerScope);
        parameters.AddWithValue("@owner", key.OwnerId);
        parameters.AddWithValue("@npc", key.NpcId);
        parameters.AddWithValue("@item", key.ItemId);
        parameters.AddWithValue("@period", key.Period);
    }
}
