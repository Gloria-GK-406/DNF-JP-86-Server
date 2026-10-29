using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Schema;

public sealed class RetiredColumnMigrationTests
{
    /// <summary>Every retired column, with the declaration it had before it was taken out.</summary>
    private static readonly (string Table, string Column, string Declaration)[] Retired =
    [
        ("mailbox_messages", "gold", "INTEGER NOT NULL DEFAULT 0"),
        ("mailbox_recipients", "received_gold_flag", "INTEGER NOT NULL DEFAULT 0"),
        ("marriage_relations", "love_points", "INTEGER NOT NULL DEFAULT 0 CHECK(love_points>=0)"),
        ("marriage_weddings", "love_points", "INTEGER NOT NULL DEFAULT 0 CHECK(love_points>=0)"),
        ("accounts", "cube_black", "INTEGER NOT NULL DEFAULT 0"),
        ("accounts", "cube_white", "INTEGER NOT NULL DEFAULT 0"),
        ("accounts", "cube_red", "INTEGER NOT NULL DEFAULT 0"),
        ("accounts", "cube_blue", "INTEGER NOT NULL DEFAULT 0"),
        ("accounts", "cube_clear", "INTEGER NOT NULL DEFAULT 0"),
        ("accounts", "cube_gold", "INTEGER NOT NULL DEFAULT 0"),
        ("accounts", "soul_common", "INTEGER NOT NULL DEFAULT 0"),
        ("accounts", "soul_advanced", "INTEGER NOT NULL DEFAULT 0"),
        ("accounts", "soul_rare", "INTEGER NOT NULL DEFAULT 0"),
        ("accounts", "soul_unique", "INTEGER NOT NULL DEFAULT 0"),
        ("accounts", "soul_legendary", "INTEGER NOT NULL DEFAULT 0"),
        ("characters", "skill_tree_index", "INTEGER NOT NULL DEFAULT -1"),
        ("characters", "appearance_blob", "BLOB"),
        ("items", "cargo_character_id", "INTEGER"),
        ("account_cargo_state", "item_count", "INTEGER NOT NULL DEFAULT 0"),
    ];

    [Fact]
    public void A_new_database_is_built_without_any_retired_column()
    {
        using var db = new Database();
        Assert.Equal(0, db.RetiredColumnCount);
    }

    /// <summary>
    /// An old database loses every retired column on startup, keeps its rows, and a second startup changes nothing.
    /// </summary>
    [Fact]
    public void An_old_database_loses_every_retired_column_and_keeps_its_rows()
    {
        using var db = Database.Legacy();
        db.Sql("""
            INSERT INTO accounts(account_id,m_id) VALUES(1,'old');
            INSERT INTO characters(character_id,account_id,name,job,appearance_blob) VALUES(10,1,'old',1,X'0102');
            INSERT INTO items(item_uid,item_core,cargo_character_id) VALUES(5,zeroblob(82),10);
            INSERT INTO account_cargo_state(account_id,selection_key,value32,item_count) VALUES(1,2,300,7);
            """);
        Assert.Equal(Retired.Length, db.RetiredColumnCount);

        db.Open();
        db.Open();

        Assert.Equal(0, db.RetiredColumnCount);
        Assert.Equal(1, db.Scalar("SELECT COUNT(*) FROM characters WHERE character_id=10 AND name='old'"));
        Assert.Equal(1, db.Scalar("SELECT COUNT(*) FROM items WHERE item_uid=5"));
        Assert.Equal(300, db.Scalar("SELECT value32 FROM account_cargo_state WHERE account_id=1"));
        Assert.Equal("ok", db.Text("PRAGMA integrity_check"));
    }

    /// <summary>
    /// A character whose pages were only ever described by the old index reads the same pages after it is gone.
    /// </summary>
    /// <remarks>
    /// Before, a character with no page rows was read as pages 0, 1 and any page holding skills, page 1 unlocked
    /// when the index was 0 or more, and the index as the active page; a character with page rows but no active
    /// page took the index as its active page. The expected values are what that read gave; after the column goes
    /// the server's own page table has to give the same.
    /// </remarks>
    [Fact]
    public async Task Skill_pages_known_only_from_the_old_index_are_kept()
    {
        using var db = Database.Legacy();
        db.Sql("""
            INSERT INTO accounts(account_id,m_id) VALUES(1,'old');
            INSERT INTO characters(character_id,account_id,name,job,skill_tree_index)
                VALUES(11,1,'second',1,1),(12,1,'first',1,0),(13,1,'locked',1,-1),(14,1,'rows',1,1);
            INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level)
                VALUES(11,'pve',0,0,1,1),(11,'pve',2,0,1,1);
            INSERT INTO character_skill_builds VALUES(14,0,1),(14,1,1);
            """);

        db.Open();

        Assert.Equal(0, db.RetiredColumnCount);
        Assert.Equal(
            ["11 active 1 unlocked 0,1,2", "12 active 0 unlocked 0,1", "13 active 0 unlocked 0", "14 active 1 unlocked 0,1"],
            await db.SkillPagesAsync(11, 12, 13, 14));
    }

    private sealed class Database : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"retired-columns-{Guid.NewGuid():N}.db");

        public Database() => Open();

        /// <summary>A database carrying every retired column, as one built before they were retired would.</summary>
        public static Database Legacy()
        {
            var db = new Database();
            db.Sql(string.Concat(Retired.Select(column =>
                $"ALTER TABLE {column.Table} ADD COLUMN {column.Column} {column.Declaration};")));
            return db;
        }

        public void Open() => _ = new SqliteDatabase(path, new DomainTransactionScope());

        public long RetiredColumnCount => Retired.Sum(column => Scalar(
            $"SELECT COUNT(*) FROM pragma_table_info('{column.Table}') WHERE name='{column.Column}'"));

        /// <summary>Each character's active page and unlocked pages, as the server reads them.</summary>
        public async Task<string[]> SkillPagesAsync(params int[] characters)
        {
            var table = new SqliteCharacterSkillBuildTable(new SqliteDatabase(path, new DomainTransactionScope()));
            var pages = new List<string>();
            foreach (var character in characters)
            {
                var builds = await table.ReadAsync(new CharacterId(character), default);
                pages.Add($"{character} active {builds.Active.Value} unlocked "
                    + string.Join(',', builds.Builds.Where(build => build.IsUnlocked).Select(build => build.Id.Value)));
            }
            return [.. pages];
        }

        public void Sql(string sql)
        {
            using var connection = Connect();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public long Scalar(string sql) => Convert.ToInt64(Execute(sql));

        public string Text(string sql) => Convert.ToString(Execute(sql))!;

        private object? Execute(string sql)
        {
            using var connection = Connect();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        private SqliteConnection Connect()
        {
            var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            return connection;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
