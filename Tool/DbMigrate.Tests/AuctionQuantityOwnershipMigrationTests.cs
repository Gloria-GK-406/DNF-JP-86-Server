using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Schema;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Auction;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DbMigrate.Tests;

public sealed class AuctionQuantityOwnershipMigrationTests
{
    [Fact]
    public async Task Upgrade_keeps_custody_history_extensions_and_sequence_without_a_live_quantity_copy()
    {
        var path = OldDatabase();
        string? backup = null;
        try
        {
            Execute(path, """
                ALTER TABLE auction_listings ADD COLUMN custom_note TEXT;
                CREATE INDEX custom_listing_note ON auction_listings(custom_note);
                UPDATE auction_listings SET custom_note='keep';
                UPDATE sqlite_sequence SET seq=1000 WHERE name='auction_listings';
                """);
            var result = Migrator.Migrate(path);
            backup = result.Backup;
            Assert.NotNull(backup);
            Assert.True(File.Exists(backup));
            Assert.Equal((10L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM pragma_table_info('auction_listings') WHERE name='quantity_remaining'"));
            Assert.Equal(1, Scalar(path, "SELECT quantity_at_close IS NULL FROM auction_listings WHERE listing_id=1"));
            Assert.Equal(18, Scalar(path, "SELECT quantity_at_close FROM auction_listings WHERE listing_id=2"));
            Assert.Equal(1, Scalar(path, "SELECT quantity_at_close FROM auction_listings WHERE listing_id=3"));
            Assert.Equal(3000, Scalar(path, "SELECT buyout_price FROM auction_listings WHERE listing_id=1"));
            Assert.Equal(18, Scalar(path, "SELECT item_count FROM auction_custody WHERE listing_id=1"));
            Assert.Equal(12, Scalar(path, "SELECT quantity FROM auction_trades WHERE listing_id=1"));
            Assert.Equal(3, Scalar(path, "SELECT COUNT(*) FROM auction_listings WHERE custom_note='keep'"));
            Assert.Equal(1000, Scalar(path, "SELECT seq FROM sqlite_sequence WHERE name='auction_listings'"));
            Assert.Equal(1, Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE name='custom_listing_note'"));
            Assert.Equal(CharacterDatabaseSchema.Version, Scalar(path, "PRAGMA user_version"));
            Assert.Equal((result.To, result.To, (string?)null), Decompose(Migrator.Migrate(path)));
            var listings = new SqliteAuctionDomainStore(Unversioned.Open(path));
            var active = (await listings.FindAsync(new(1), default))!;
            var closed = (await listings.FindAsync(new(2), default))!;
            var gold = (await listings.FindAsync(new(3), default))!;
            Assert.Equal((30, 18, 1800L), (active.QuantityInitial, active.QuantityRemaining, active.Pricing.BuyoutPrice!.Value));
            Assert.Equal((30, 18, 1800L), (closed.QuantityInitial, closed.QuantityRemaining, closed.Pricing.BuyoutPrice!.Value));
            Assert.Equal((1, 1, 100L), (gold.QuantityInitial, gold.QuantityRemaining, gold.Pricing.BuyoutPrice!.Value));
        }
        finally { Cleanup(path, backup); }
    }

    [Theory]
    [InlineData("DELETE FROM auction_custody")]
    [InlineData("UPDATE auction_custody SET item_count=17")]
    [InlineData("UPDATE auction_custody SET item_id=999")]
    [InlineData("UPDATE auction_custody SET item_core=zeroblob(82)")]
    public void Disagreement_aborts_before_schema_or_data_changes(string corruption)
    {
        var path = OldDatabase();
        try
        {
            Execute(path, corruption);
            var error = Assert.Throws<InvalidDataException>(() => Migrator.Migrate(path, backup: false));
            Assert.Contains("1", error.Message, StringComparison.Ordinal);
            Assert.Equal(10, Scalar(path, "PRAGMA user_version"));
            Assert.Equal(18, Scalar(path, "SELECT quantity_remaining FROM auction_listings WHERE listing_id=1"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM pragma_table_info('auction_listings') WHERE name='quantity_at_close'"));
        }
        finally { Cleanup(path); }
    }

    private static (long, long, string?) Decompose(Migrator.Result result) => (result.From, result.To, result.Backup);

    private static string OldDatabase()
    {
        var path = Unversioned.NewPath("auction-quantity-v10");
        _ = Unversioned.Open(path);
        Execute(path, """
            ALTER TABLE auction_listings DROP COLUMN quantity_at_close;
            ALTER TABLE auction_listings ADD COLUMN quantity_remaining INTEGER NOT NULL DEFAULT 1 CHECK(quantity_remaining>=0 AND quantity_remaining<=quantity_initial);
            PRAGMA user_version=10;
            """);
        Execute(path, """
            INSERT INTO auction_listings(listing_id,market,state,seller_character_id,seller_account_id,seller_name,item_id,quantity_initial,quantity_remaining,buyout_price,unit_price,fee_rate_bp,created_at,expires_at)
            VALUES(1,0,0,71,1,X'61',3037,30,18,1800,100,500,1,2),
                  (2,0,1,71,1,X'61',3037,30,18,1800,100,500,1,2),
                  (3,1,2,71,1,X'61',2681725,1,1,100,NULL,500,1,2);
            INSERT INTO auction_trades(listing_id,market,item_id,quantity,price,fee,seller_character_id,buyer_character_id,settled_at)
            VALUES(1,0,3037,12,1200,60,71,72,1);
            """);
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO auction_custody(listing_id,kind,item_uid,item_id,item_count,item_core) VALUES(1,'stackable',0,3037,18,@core)";
        var snapshot = new ItemSnapshot { ItemId = 3037, LegacyKind = 2, Type = ItemTraitClassification.For(2, 3037), IsPile = true, Count = 30 };
        command.Parameters.AddWithValue("@core", InventoryItemCore.Encode(snapshot.ToCore()));
        command.ExecuteNonQuery();
        return path;
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Cleanup(string path, string? backup = null)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        if (backup is not null) File.Delete(backup);
    }
}
