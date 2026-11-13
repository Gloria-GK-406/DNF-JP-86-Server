using Microsoft.Data.Sqlite;

namespace DbMigrate;

/// <summary>Adds the auction house tables: listings, item custody and trades (schema 6 to 7).</summary>
/// <remarks>
/// The statements are copied from the version-7 declaration rather than read from it, so this step
/// keeps producing the version-7 shape after the declaration moves on. No existing row is read or changed.
/// </remarks>
internal static class AuctionTablesUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS auction_listings(listing_id INTEGER PRIMARY KEY AUTOINCREMENT,market INTEGER NOT NULL CHECK(market IN(0,1)),state INTEGER NOT NULL CHECK(state BETWEEN 0 AND 3),seller_character_id INTEGER NOT NULL,seller_account_id INTEGER NOT NULL,seller_name BLOB NOT NULL,item_id INTEGER NOT NULL CHECK(item_id>0),quantity_initial INTEGER NOT NULL CHECK(quantity_initial>0),quantity_remaining INTEGER NOT NULL CHECK(quantity_remaining>=0 AND quantity_remaining<=quantity_initial),gold_amount INTEGER NOT NULL DEFAULT 0 CHECK(gold_amount>=0),start_price INTEGER CHECK(start_price>0),buyout_price INTEGER CHECK(buyout_price>0),unit_price INTEGER CHECK(unit_price>0),current_bid INTEGER CHECK(current_bid>0),top_bidder_character_id INTEGER,top_bidder_account_id INTEGER,deposit INTEGER NOT NULL DEFAULT 0 CHECK(deposit>=0),fee_rate_bp INTEGER NOT NULL CHECK(fee_rate_bp BETWEEN 0 AND 10000),created_at INTEGER NOT NULL,expires_at INTEGER NOT NULL,closed_at INTEGER,display_core BLOB,CHECK(start_price IS NOT NULL OR buyout_price IS NOT NULL),CHECK((current_bid IS NULL)=(top_bidder_character_id IS NULL)));
            CREATE INDEX IF NOT EXISTS ix_auction_listings_due ON auction_listings(state,expires_at);
            CREATE INDEX IF NOT EXISTS ix_auction_listings_seller ON auction_listings(seller_character_id,state);
            CREATE INDEX IF NOT EXISTS ix_auction_listings_bidder ON auction_listings(top_bidder_character_id,state);
            CREATE INDEX IF NOT EXISTS ix_auction_listings_item ON auction_listings(market,state,item_id);
            CREATE TABLE IF NOT EXISTS auction_custody(listing_id INTEGER PRIMARY KEY REFERENCES auction_listings(listing_id),kind TEXT NOT NULL CHECK(kind IN('equipment','stackable')),item_uid INTEGER NOT NULL DEFAULT 0,item_id INTEGER NOT NULL,item_count INTEGER NOT NULL CHECK(item_count>0),item_core BLOB,CHECK(kind='stackable' OR (item_uid>0 AND item_count=1)),CHECK(kind='equipment' OR (item_uid=0 AND item_core IS NOT NULL)));
            CREATE UNIQUE INDEX IF NOT EXISTS ux_auction_custody_equipment ON auction_custody(item_uid) WHERE kind='equipment';
            CREATE TABLE IF NOT EXISTS auction_trades(trade_id INTEGER PRIMARY KEY AUTOINCREMENT,listing_id INTEGER NOT NULL,market INTEGER NOT NULL,item_id INTEGER NOT NULL,quantity INTEGER NOT NULL CHECK(quantity>0),price INTEGER NOT NULL CHECK(price>0),fee INTEGER NOT NULL CHECK(fee>=0),seller_character_id INTEGER NOT NULL,buyer_character_id INTEGER NOT NULL,settled_at INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_auction_trades_item ON auction_trades(market,item_id,settled_at);
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
