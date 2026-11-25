using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Persistence.Pvp;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

/// <summary>Schema 13 to 14: <c>character_pvp</c> splits, and no grade or seeded skill page moves.</summary>
public sealed class PvpRecordMigrationTests
{
    /// <summary>The version-13 declaration of <c>character_pvp</c>, as a version-13 server built it.</summary>
    private const string VersionThirteenPvp =
        "CREATE TABLE character_pvp ( character_id INTEGER PRIMARY KEY, pvp_grade INTEGER NOT NULL DEFAULT 0, pvp_lose_streak INTEGER NOT NULL DEFAULT 0, pvp_rank_point INTEGER NOT NULL DEFAULT 0, pvp_rating_grade INTEGER NOT NULL DEFAULT 0, pvp_stat_a INTEGER NOT NULL DEFAULT 0, pvp_win_streak INTEGER NOT NULL DEFAULT 0, skills_initialized INTEGER NOT NULL DEFAULT 0, FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE );";

    [Fact]
    public async Task Version_thirteen_upgrade_splits_character_pvp_and_keeps_grades_and_seeded_skills()
    {
        var path = Unversioned.NewPath("pvp-record-v13");
        var fresh = Unversioned.NewPath("pvp-record-fresh");
        try
        {
            _ = Unversioned.Open(fresh);
            _ = Unversioned.Open(path);
            Execute(path, $"""
                DROP TABLE character_pvp_record;
                DROP TABLE character_pvp_skill_state;
                DROP TABLE character_pvp_legacy;
                DROP TABLE pvp_match_results;
                DROP TABLE pvp_matches;
                {VersionThirteenPvp}
                PRAGMA user_version=13;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,slot_index) VALUES
                    (71,7,'plain',0,0,70,0,0),(72,7,'graded',1,0,70,0,1),(73,7,'odd',2,0,70,0,2),(74,7,'none',3,0,70,0,3);
                INSERT INTO character_pvp(character_id,pvp_grade,pvp_rating_grade,skills_initialized,pvp_rank_point,pvp_stat_a,pvp_win_streak,pvp_lose_streak) VALUES
                    (71,0,0,0,0,0,0,0),
                    (72,7,3,1,0,0,0,0),
                    (73,12,5,1,4294967296,22,3,4);
                """);

            var migrated = Migrator.Migrate(path, backup: false);

            Assert.Equal((13L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(ShapeOf(fresh), ShapeOf(path));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE name='character_pvp'"));
            Assert.Equal(
                new[] { "71|0|0||0|0|0|0|0", "72|7|3||0|0|0|0|0", "73|12|5||0|0|0|0|0" },
                Rows(path, "SELECT character_id,grade,rating_grade,grade_experience,win,lose,draw,win_streak,peak_win_streak FROM character_pvp_record ORDER BY character_id"));
            Assert.Equal(
                new[] { "71|0", "72|1", "73|1" },
                Rows(path, "SELECT character_id,skills_initialized FROM character_pvp_skill_state ORDER BY character_id"));
            // Only the row whose unnamed columns held anything, as they were.
            Assert.Equal(
                new[] { "73|4294967296|22|3|4" },
                Rows(path, "SELECT character_id,rank_point,stat_a,win_streak,lose_streak FROM character_pvp_legacy"));
            // A character that had no row is not given one.
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM character_pvp_record WHERE character_id=74"));

            // The server opens it, and the store reads a migrated record as a new one at its grade.
            var store = new SqlitePvpRecordDomainStore(Unversioned.Open(path));
            Assert.Equal(new PvpRecord(7, 3, null, 0, 0, 0, 0, 0), await store.ReadAsync(new(72), default));

            var again = Migrator.Migrate(path, backup: false);
            Assert.Equal(again.From, again.To);
            Assert.Equal(ShapeOf(fresh), ShapeOf(path));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            File.Delete(fresh);
        }
    }

    [Fact]
    public void Version_fourteen_defaults_the_duel_build_and_keeps_ordinary_build_and_skill_rows()
    {
        var path = Unversioned.NewPath("pvp-build-v14");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                ALTER TABLE character_pvp_skill_state DROP COLUMN active_skill_build;
                PRAGMA user_version=14;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,active_skill_build)
                    VALUES(71,7,'kept',0,0,70,0,1);
                INSERT INTO character_pvp_skill_state(character_id,skills_initialized) VALUES(71,1);
                INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level)
                    VALUES(71,'pve',1,7,1,3),(71,'pvp',1,2,1,2);
                """);

            var migrated = Migrator.Migrate(path, backup: false);

            Assert.Equal((14L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(new[] { "1|0" }, Rows(path, """
                SELECT c.active_skill_build,p.active_skill_build
                FROM characters c JOIN character_pvp_skill_state p USING(character_id)
                WHERE c.character_id=71 AND p.skills_initialized=1
                """));
            Assert.Equal(new[] { "pve|1|7|3", "pvp|1|2|2" }, Rows(path, """
                SELECT mode,page_index,slot,level FROM character_skills WHERE character_id=71 ORDER BY mode
                """));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    /// <summary>
    /// A database that came through the legacy step already has the new tables, built from the
    /// current declaration, and keeps its <c>character_pvp</c> until this step moves it.
    /// </summary>
    [Fact]
    public void An_unversioned_database_with_character_pvp_arrives_split()
    {
        var path = Unversioned.NewPath("pvp-record-legacy");
        try
        {
            Unversioned.CreateUnstamped(path);
            Execute(path, $"""
                {VersionThirteenPvp}
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,slot_index) VALUES(71,7,'kept',0,0,70,0,0);
                INSERT INTO character_pvp(character_id,pvp_grade,pvp_rating_grade,skills_initialized,pvp_stat_a) VALUES(71,9,2,1,5);
                """);

            Unversioned.Migrate(path);

            Assert.Equal(new[] { "71|9|2|" }, Rows(path, "SELECT character_id,grade,rating_grade,grade_experience FROM character_pvp_record"));
            Assert.Equal(new[] { "71|1" }, Rows(path, "SELECT character_id,skills_initialized FROM character_pvp_skill_state"));
            Assert.Equal(new[] { "71|0|5|0|0" }, Rows(path, "SELECT character_id,rank_point,stat_a,win_streak,lose_streak FROM character_pvp_legacy"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE name='character_pvp'"));
            _ = Unversioned.Open(path);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static string[] ShapeOf(string path) => Rows(path, """
        SELECT m.name||'.'||c.name||':'||c.type||':'||c."notnull"||':'||IFNULL(c.dflt_value,'')||':'||c.pk
        FROM sqlite_master m JOIN pragma_table_info(m.name) c
        WHERE m.type='table' AND m.name NOT LIKE 'sqlite_%'
        UNION ALL
        SELECT m.type||' '||m.name||' on '||m.tbl_name||': '||IFNULL(m.sql,'')
        FROM sqlite_master m WHERE m.type IN ('index','trigger')
        ORDER BY 1
        """);

    private static string[] Rows(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount)
                .Select(index => reader.IsDBNull(index) ? "" : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture))));
        return [.. rows];
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
}
