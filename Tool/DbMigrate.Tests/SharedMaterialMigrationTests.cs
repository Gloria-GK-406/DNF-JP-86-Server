using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

public sealed class SharedMaterialMigrationTests
{
    private const int SharedList = 240;

    [Fact]
    public void LegacyAccountCargoIndexIsRetiredBeforeTheSharedPileIsInserted()
    {
        using var db = new LegacyDatabase();
        db.Sql("INSERT INTO accounts(account_id,m_id,cube_black) VALUES(1,'indexed',5);");
        using (var command = db.Connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO items(item_core) VALUES(@core);"
                + "INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) "
                + "VALUES('account',1,12,0,'template',2000,last_insert_rowid(),3);"
                + "CREATE UNIQUE INDEX idx_item_slots_account_slot ON item_slots(owner_id,slot_index) WHERE owner_scope='account';";
            command.Parameters.AddWithValue("@core", InventoryItemCore.Encode(StackableItemCore.Create(3, 2000, 3)));
            command.ExecuteNonQuery();
        }
        db.Ensure();
        Assert.Equal(2, db.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_id=1 AND slot_index=0"));
        Assert.Equal(3, db.Scalar("SELECT count FROM item_slots WHERE owner_id=1 AND list_type=12 AND slot_index=0"));
        Assert.Equal(5, db.Scalar($"SELECT count FROM item_slots WHERE owner_id=1 AND list_type={SharedList} AND slot_index=0"));
    }

    [Fact]
    public void EveryNonzeroBalanceBecomesAnOrdinaryPileAndRepeatedStartupDoesNotCreditItAgain()
    {
        using var db = new LegacyDatabase();
        db.Sql("INSERT INTO accounts(account_id,m_id,cube_black,cube_white,cube_red,cube_blue,cube_clear,cube_gold,"
            + "soul_common,soul_advanced,soul_rare,soul_unique,soul_legendary) "
            + "VALUES(1,'first',1,2,3,4,2147483647,6,7,8,9,10,11),(2,'second',0,0,0,0,37,0,0,0,0,0,0)");

        db.Ensure();
        Assert.Equal(12, db.Scalar($"SELECT COUNT(*) FROM item_slots WHERE list_type={SharedList}"));
        foreach (var (definition, index) in AccountMaterialCatalog.Definitions.Select((value, index) => (value, index)))
        {
            using var command = db.Connection.CreateCommand();
            command.CommandText = $"SELECT s.kind,s.id,s.count,i.item_core FROM item_slots s JOIN items i ON i.item_uid=s.core_uid "
                + $"WHERE s.owner_scope='account' AND s.owner_id=1 AND s.list_type={SharedList} AND s.slot_index={index}";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            var count = index == 4 ? int.MaxValue : index + 1;
            Assert.Equal("template", reader.GetString(0));
            Assert.Equal(definition.ItemId, reader.GetInt32(1));
            Assert.Equal(count, reader.GetInt32(2));
            Assert.Equal(StackableItemCore.Create(definition.ItemKind, definition.ItemId, count),
                InventoryItemCore.Decode((byte[])reader.GetValue(3)));
            Assert.False(reader.Read());
        }
        db.Sql($"UPDATE item_slots SET count=36 WHERE owner_id=2 AND list_type={SharedList}");
        db.Ensure();
        Assert.Equal(36, db.Scalar($"SELECT count FROM item_slots WHERE owner_id=2 AND list_type={SharedList}"));
        Assert.Equal(12, db.Scalar("SELECT COUNT(*) FROM items"));
        Assert.Equal(0, db.LegacyColumnCount);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(2147483648L)]
    public void InvalidBalancesRefuseMigrationWithoutCreditingEarlierAccounts(long invalidCount)
    {
        using var db = new LegacyDatabase();
        db.Sql($"INSERT INTO accounts(account_id,m_id,cube_clear) VALUES(1,'valid',5),(2,'invalid',{invalidCount})");
        Assert.Throws<InvalidDataException>(db.Ensure);
        Assert.Equal(0, db.Scalar("SELECT COUNT(*) FROM items"));
        Assert.Equal(0, db.MarkerCount);
        Assert.Equal(invalidCount, db.Scalar("SELECT cube_clear FROM accounts WHERE account_id=2"));
    }

    [Fact]
    public void AFailedInsertRollsBackAllPilesAndTheMarkerAndAllowsARetry()
    {
        using var db = new LegacyDatabase();
        db.Sql("INSERT INTO accounts(account_id,m_id,cube_black,cube_white) VALUES(1,'retry',5,7);"
            + "CREATE TRIGGER refuse_second_material BEFORE INSERT ON item_slots WHEN NEW.owner_scope='account' "
            + $"AND NEW.list_type={SharedList} AND NEW.slot_index=1 BEGIN SELECT RAISE(ABORT,'forced material insert'); END;");
        Assert.Throws<SqliteException>(db.Ensure);
        Assert.Equal(0, db.Scalar("SELECT COUNT(*) FROM items"));
        Assert.Equal(0, db.Scalar("SELECT COUNT(*) FROM item_slots"));
        Assert.Equal(0, db.MarkerCount);
        Assert.Equal(7, db.Scalar("SELECT cube_white FROM accounts WHERE account_id=1"));
        db.Sql("DROP TRIGGER refuse_second_material");
        db.Ensure();
        db.Ensure();
        Assert.Equal(2, db.Scalar("SELECT COUNT(*) FROM item_slots"));
        Assert.Equal(2, db.Scalar("SELECT COUNT(*) FROM items"));
        Assert.Equal(1, db.Scalar("PRAGMA user_version"));
    }

    [Theory]
    [InlineData("account", 12)]
    [InlineData("character", 0)]
    [InlineData("account", SharedList)]
    public void ExistingMaterialCustodyIsReportedInsteadOfGuessingAMerge(string scope, int list)
    {
        using var db = new LegacyDatabase();
        db.Sql("INSERT INTO accounts(account_id,m_id,cube_clear) VALUES(1,'conflict',5);"
            + "INSERT INTO characters(character_id,account_id,name) VALUES(1,1,'holder')");
        using (var command = db.Connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO items(item_core) VALUES(@core);"
                + "INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) "
                + $"VALUES('{scope}',1,{list},4,'template',3037,last_insert_rowid(),3)";
            command.Parameters.AddWithValue("@core", InventoryItemCore.Encode(StackableItemCore.Create(3, 3037, 3)));
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(db.Ensure);
        Assert.Equal(1, db.Scalar("SELECT COUNT(*) FROM items"));
        Assert.Equal(1, db.Scalar("SELECT COUNT(*) FROM item_slots"));
        Assert.Equal(0, db.MarkerCount);
        Assert.Equal(5, db.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
    }

    /// <summary>A database from before the migration: balances in account columns and no marker.</summary>
    private sealed class LegacyDatabase : IDisposable
    {
        private static readonly string[] LegacyColumns =
        [
            "cube_black", "cube_white", "cube_red", "cube_blue", "cube_clear", "cube_gold",
            "soul_common", "soul_advanced", "soul_rare", "soul_unique", "soul_legendary",
        ];

        private readonly string path = Path.Combine(Path.GetTempPath(), $"dfo-material-migration-{Guid.NewGuid():N}.db");
        public SqliteConnection Connection { get; }
        public LegacyDatabase()
        {
            Unversioned.CreateUnstamped(path);
            Connection = new SqliteConnection($"Data Source={path}");
            Connection.Open();
            Sql(string.Concat(LegacyColumns.Select(
                column => $"ALTER TABLE accounts ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0;")));
        }
        public long LegacyColumnCount => Scalar("SELECT COUNT(*) FROM pragma_table_info('accounts') WHERE name IN ("
            + string.Join(',', LegacyColumns.Select(column => $"'{column}'")) + ")");
        public void Ensure() => Unversioned.Migrate(path);
        public long MarkerCount => Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name='schema_migrations'") == 0
            ? 0 : Scalar("SELECT COUNT(*) FROM schema_migrations WHERE name='account-shared-material-v1'");
        public void Sql(string sql) { using var command = Connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
        public long Scalar(string sql) { using var command = Connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(command.ExecuteScalar()); }
        public void Dispose() { Connection.Dispose(); SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
