using Microsoft.Data.Sqlite;
using Dfo.Server.Persistence.Inventory;

namespace DbMigrate;

/// <summary>Moves live item quantities to custody and retains only closure snapshots (schema 10 to 11).</summary>
internal static class AuctionQuantityOwnershipUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        // The legacy bootstrap can already have built the current shape for an absent table.
        if (!Shape.HasColumn(connection, transaction, "auction_listings", "quantity_remaining"))
        {
            transaction.Commit();
            return;
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT l.listing_id FROM auction_listings l LEFT JOIN auction_custody c ON c.listing_id=l.listing_id
            WHERE l.market=0 AND l.state=0 AND
                (c.listing_id IS NULL OR c.item_id<>l.item_id OR c.item_count<>l.quantity_remaining
                 OR c.item_count<=0 OR c.item_count>l.quantity_initial)
            ORDER BY l.listing_id LIMIT 1
            """;
        if (command.ExecuteScalar() is { } invalid)
            throw new InvalidDataException($"Auction listing {invalid} disagrees with its item custody; migration was not applied.");
        command.CommandText = """
            SELECT l.listing_id,l.item_id,c.kind,c.item_core,i.item_core,
                EXISTS(SELECT 1 FROM item_slots s WHERE s.core_uid=c.item_uid)
            FROM auction_listings l JOIN auction_custody c ON c.listing_id=l.listing_id
            LEFT JOIN items i ON c.kind='equipment' AND i.item_uid=c.item_uid
            WHERE l.market=0 AND l.state=0 ORDER BY l.listing_id
            """;
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var equipment = reader.GetString(2) == "equipment";
                var ordinal = equipment ? 4 : 3;
                if (reader.IsDBNull(ordinal) || reader.GetValue(ordinal) is not byte[] core
                    || core.Length != InventoryItemCore.Size || InventoryItemCore.Decode(core).ItemId != reader.GetInt32(1)
                    || equipment && reader.GetBoolean(5))
                    throw new InvalidDataException($"Auction listing {reader.GetInt64(0)} has invalid item custody; migration was not applied.");
            }
        // ALTER preserves unknown columns, indexes, references and the AUTOINCREMENT high-water mark.
        command.CommandText = """
            ALTER TABLE auction_listings ADD COLUMN quantity_at_close INTEGER CHECK(quantity_at_close>=0 AND quantity_at_close<=quantity_initial AND (state<>0 OR quantity_at_close IS NULL));
            UPDATE auction_listings SET quantity_at_close=quantity_remaining WHERE state<>0;
            UPDATE auction_listings SET buyout_price=unit_price*quantity_initial WHERE market=0 AND unit_price IS NOT NULL;
            ALTER TABLE auction_listings DROP COLUMN quantity_remaining;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
