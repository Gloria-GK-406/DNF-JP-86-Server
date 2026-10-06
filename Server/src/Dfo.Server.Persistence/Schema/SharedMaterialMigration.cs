using System.Buffers.Binary;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Schema;

internal static class SharedMaterialMigration
{
    private const string Name = "account-shared-material-v1";

    public static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var marker = connection.CreateCommand();
        marker.Transaction = transaction;
        marker.CommandText = "SELECT 1 FROM schema_migrations WHERE name=@name";
        marker.Parameters.AddWithValue("@name", Name);
        if (marker.ExecuteScalar() is not null)
        {
            transaction.Commit();
            return;
        }

        RejectExistingCustody(connection, transaction);
        var balances = ReadBalances(connection, transaction);
        foreach (var (account, slot, count) in balances)
        {
            var itemId = AccountMaterialCatalog.Definitions[slot].ItemId;
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO items(item_core) VALUES(@core);
                INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                VALUES('account',@account,@list,@slot,'template',@item,last_insert_rowid(),@count);
                """;
            insert.Parameters.AddWithValue("@core", InventoryItemCore.Encode(StackableItemCore.Create(AccountMaterialCatalog.Definitions[slot].ItemKind, itemId, count)));
            insert.Parameters.AddWithValue("@account", account);
            insert.Parameters.AddWithValue("@list", (int)InventoryContainerType.SharedMaterial);
            insert.Parameters.AddWithValue("@slot", slot);
            insert.Parameters.AddWithValue("@item", itemId);
            insert.Parameters.AddWithValue("@count", count);
            insert.ExecuteNonQuery();
        }
        marker.CommandText = "INSERT INTO schema_migrations(name) VALUES(@name)";
        marker.ExecuteNonQuery();
        transaction.Commit();
    }

    private static List<(int Account, int Slot, int Count)> ReadBalances(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT account_id,cube_black,cube_white,cube_red,cube_blue,cube_clear,cube_gold,
                   soul_common,soul_advanced,soul_rare,soul_unique,soul_legendary
            FROM accounts ORDER BY account_id;
            """;
        using var reader = command.ExecuteReader();
        var balances = new List<(int, int, int)>();
        while (reader.Read())
        {
            var account = reader.GetInt64(0);
            if (account is <= 0 or > int.MaxValue)
                throw new InvalidDataException($"Shared material migration: invalid account owner {account}.");
            for (var slot = 0; slot < AccountMaterialCatalog.Definitions.Count; slot++)
            {
                if (reader.GetValue(slot + 1) is not long count || count is < 0 or > int.MaxValue)
                    throw new InvalidDataException($"Shared material migration: account {account}, material {AccountMaterialCatalog.Definitions[slot].ItemId} has an invalid balance.");
                if (count > 0) balances.Add(((int)account, slot, (int)count));
            }
        }
        return balances;
    }

    private static void RejectExistingCustody(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT s.owner_scope,s.owner_id,s.list_type,s.slot_index,i.item_core
            FROM item_slots s LEFT JOIN items i ON i.item_uid=s.core_uid;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var shared = reader.GetInt32(2) == (int)InventoryContainerType.SharedMaterial;
            var material = !reader.IsDBNull(4) && reader.GetValue(4) is byte[] { Length: >= 5 } core
                && AccountMaterialCatalog.ContainsItem(BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1, 4)));
            if (shared || material)
                throw new InvalidDataException($"Shared material migration conflicts with existing custody at {reader.GetString(0)}:{reader.GetInt64(1)}, list {reader.GetInt32(2)}, slot {reader.GetInt32(3)}; reconcile its provenance before migration.");
        }
    }
}
