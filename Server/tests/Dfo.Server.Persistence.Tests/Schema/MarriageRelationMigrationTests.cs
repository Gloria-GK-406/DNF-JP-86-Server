using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Marriage;
using Microsoft.Data.Sqlite;
namespace Dfo.Server.Persistence.Tests.Schema;
public sealed class MarriageRelationMigrationTests
{
    private const string OldShape = """
        CREATE TABLE marriage_proposals (
            id TEXT PRIMARY KEY, payload_json TEXT NOT NULL, state INTEGER NOT NULL CHECK(state BETWEEN 0 AND 3),
            revision INTEGER NOT NULL, created_at INTEGER NOT NULL, response INTEGER);
        CREATE TABLE marriage_proposal_participants (
            proposal_id TEXT NOT NULL REFERENCES marriage_proposals(id), role INTEGER NOT NULL CHECK(role IN(0,1)),
            character_id INTEGER NOT NULL, active INTEGER NOT NULL CHECK(active IN(0,1)),
            PRIMARY KEY(proposal_id,role), UNIQUE(proposal_id,character_id));
        CREATE TABLE marriage_ceremonies (
            id TEXT PRIMARY KEY, relation_id TEXT NOT NULL REFERENCES marriage_proposals(id), partner_character_id INTEGER NOT NULL,
            payload_json TEXT NOT NULL, tier INTEGER NOT NULL CHECK(tier=0), wait_index INTEGER NOT NULL CHECK(wait_index BETWEEN 0 AND 2),
            state INTEGER NOT NULL CHECK(state BETWEEN 0 AND 3), revision INTEGER NOT NULL CHECK(revision>=1),
            created_at INTEGER NOT NULL, expires_at INTEGER NOT NULL, response INTEGER CHECK(response BETWEEN 0 AND 2));
        CREATE TABLE marriage_weddings (
            ceremony_id TEXT PRIMARY KEY REFERENCES marriage_ceremonies(id),
            relation_id TEXT NOT NULL REFERENCES marriage_proposals(id),payload_json TEXT NOT NULL,
            phase INTEGER NOT NULL CHECK(phase BETWEEN 0 AND 2),paid_gold INTEGER NOT NULL CHECK(paid_gold>=0),
            completed_at INTEGER NOT NULL DEFAULT 0,love_points INTEGER NOT NULL DEFAULT 0 CHECK(love_points>=0));
        """;

    // One proposal per backfill case: id, proposal state, then (ceremony, ceremony state, wedding phase or -1 for none, completed_at, love points).
    private static readonly (string Id, int State, (string Id, int State, int Phase, long CompletedAt, int Love)[] Ceremonies)[] Proposals =
    [
        ("p01", 2, []),
        ("p02", 3, []),
        ("p03", 0, []),
        ("p04", 1, [("c04old", 3, 2, 0, 0), ("c04", 1, 1, 1792206195530, 7)]),
        ("p05", 1, [("c05", 1, 1, 0, 3)]),
        ("p06", 1, [("c06", 1, 0, 0, 0)]),
        ("p07", 1, [("c07old", 3, -1, 0, 0), ("c07", 0, -1, 0, 0)]),
        ("p08", 1, [("c08", 1, -1, 0, 0)]),
        ("p09", 1, [("c09", 1, 2, 0, 0)]),
        ("p10", 1, []),
        ("p11", 1, [("c11", 3, 2, 0, 0)]),
    ];

    private static readonly string[] Expected =
    [
        "p01|5|||1", "p02|5|||1", "p03|0|||1",
        "p04|4|c04|1792206195530|1", "p05|4|c05||1", "p06|3|c06||1", "p07|2|c07||1",
        "p08|1|||1", "p09|1|||1", "p10|1|||1", "p11|1|||1",
    ];

    [Fact]
    public async Task Every_backfill_case_gets_its_stage_once_and_old_tables_stay_untouched()
    {
        var path = Path.Combine(Path.GetTempPath(), $"relation-migration-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new SqliteConnection($"Data Source={path}"); db.Open(); using var q = db.CreateCommand();
            q.CommandText = OldShape + Seed(); q.ExecuteNonQuery();
            var legacy = Legacy(q);
            var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var database = new SqliteDatabase(path, new DomainTransactionScope());
            var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Assert.Equal(Expected, Relations(q));
            q.CommandText = "SELECT MIN(stage_since)||':'||MAX(stage_since) FROM marriage_relations";
            var since = ((string)q.ExecuteScalar()!).Split(':').Select(long.Parse).ToArray();
            Assert.Equal(since[0], since[1]); Assert.InRange(since[0], before, after);
            Assert.Equal(2, await new SqliteMarriageRelationDomainStore(database).CountUnsettledReservationsAsync(default));
            Assert.Equal(legacy, Legacy(q));

            q.CommandText = "UPDATE marriage_relations SET revision=9 WHERE id='p10'"; q.ExecuteNonQuery();
            var settled = Relations(q);
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            Assert.Equal(settled, Relations(q));
            Assert.Contains("p10|1|||9", settled);
            Assert.Equal(legacy, Legacy(q));
            q.CommandText = "SELECT COUNT(DISTINCT stage_since) FROM marriage_relations"; Assert.Equal(1L, q.ExecuteScalar());

            q.CommandText = "UPDATE marriage_relations SET ceremony_id=NULL WHERE id='p06'"; Assert.Throws<SqliteException>(() => q.ExecuteNonQuery());
            q.CommandText = "UPDATE marriage_relations SET stage=16 WHERE id='p01'"; Assert.Throws<SqliteException>(() => q.ExecuteNonQuery());
            q.CommandText = "PRAGMA integrity_check"; Assert.Equal("ok", q.ExecuteScalar());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    /// <summary>
    /// Shared points become each spouse's personal points once, from whichever old column is ahead, and both old
    /// columns are dropped afterwards.
    /// </summary>
    /// <remarks>
    /// A database from before relations has only the wedding's column. One from between relations and personal points
    /// also has the relation's, which a coupon may have taken past the wedding's; points only grew, so the larger wins.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Old_shared_points_are_split_from_the_larger_column_and_the_columns_are_dropped(bool relationColumn)
    {
        var path = Path.Combine(Path.GetTempPath(), $"relation-love-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new SqliteConnection($"Data Source={path}"); db.Open(); using var q = db.CreateCommand();
            q.CommandText = OldShape + Seed(
            [
                ("q1", 1, [("q1c", 1, 1, 0, 5)]),
                ("q2", 1, [("q2c", 1, 1, 0, 4)]),
                ("q3", 1, [("q3c", 1, 1, 0, 3)]),
                ("q4", 1, [("q4old", 3, 2, 0, 9), ("q4c", 0, -1, 0, 0)]),
            ]);
            if (relationColumn)
                q.CommandText += """
                    CREATE TABLE marriage_relations (
                        id TEXT PRIMARY KEY REFERENCES marriage_proposals(id),
                        stage INTEGER NOT NULL CHECK(stage BETWEEN 0 AND 15),
                        revision INTEGER NOT NULL CHECK(revision>=1),
                        stage_since INTEGER NOT NULL,
                        ceremony_id TEXT REFERENCES marriage_ceremonies(id),
                        married_at INTEGER,
                        love_points INTEGER NOT NULL DEFAULT 0 CHECK(love_points>=0),
                        CHECK((stage IN (2,3,4)) = (ceremony_id IS NOT NULL)));
                    INSERT INTO marriage_relations VALUES('q1',4,1,0,'q1c',NULL,2),('q3',4,1,0,'q3c',NULL,8);
                    """;
            q.ExecuteNonQuery();
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            _ = new SqliteDatabase(path, new DomainTransactionScope());

            // q1: the wedding's 5 beats the relation's 2. q3: the relation's 8 beats the wedding's 3 when it is there.
            var q3 = relationColumn ? 8 : 3;
            Assert.Equal([$"q1|5", "q2|4", $"q3|{q3}"],
                Rows(q, "SELECT relation_id||'|'||shared_points FROM marriage_points_migration_audit ORDER BY relation_id"));
            Assert.Equal(["100|2", "101|3", "102|2", "103|2", $"104|{q3 / 2}", $"105|{q3 - (q3 / 2)}"],
                Rows(q, "SELECT character_id||'|'||points FROM marriage_personal_points ORDER BY character_id"));
            q.CommandText = """
                SELECT COUNT(*) FROM pragma_table_info('marriage_relations') WHERE name='love_points'
                UNION ALL SELECT COUNT(*) FROM pragma_table_info('marriage_weddings') WHERE name='love_points'
                """;
            using (var reader = q.ExecuteReader())
                while (reader.Read()) Assert.Equal(0L, reader.GetInt64(0));
            q.CommandText = "PRAGMA integrity_check"; Assert.Equal("ok", q.ExecuteScalar());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    private static string Seed() => Seed(Proposals);

    [Fact]
    public void Shared_points_are_split_once_and_legacy_balance_cannot_resurrect_spent_points()
    {
        var path = Path.Combine(Path.GetTempPath(), $"personal-love-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new SqliteConnection($"Data Source={path}"); db.Open(); using var q = db.CreateCommand();
            q.CommandText = OldShape + Seed([("wallet", 1, [("wc", 1, 1, 0, 40)])]); q.ExecuteNonQuery();
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            q.CommandText = "SELECT name FROM sqlite_master WHERE name='marriage_personal_points'";
            Assert.Equal("marriage_personal_points", q.ExecuteScalar());
            q.CommandText = "SELECT points FROM marriage_personal_points ORDER BY character_id";
            using (var reader = q.ExecuteReader())
            {
                Assert.True(reader.Read()); Assert.Equal(20, reader.GetInt32(0));
                Assert.True(reader.Read()); Assert.Equal(20, reader.GetInt32(0)); Assert.False(reader.Read());
            }
            q.CommandText = "UPDATE marriage_personal_points SET points=7 WHERE character_id=100";
            q.ExecuteNonQuery();
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            q.CommandText = "SELECT points FROM marriage_personal_points WHERE character_id=100";
            Assert.Equal(7L, q.ExecuteScalar());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    private static string Seed(IEnumerable<(string Id, int State, (string Id, int State, int Phase, long CompletedAt, int Love)[] Ceremonies)> proposals)
    {
        var sql = new System.Text.StringBuilder();
        var character = 100;
        foreach (var (id, state, ceremonies) in proposals)
        {
            var active = state is 0 or 1 ? 1 : 0;
            sql.Append($"INSERT INTO marriage_proposals VALUES('{id}','{{}}',{state},1,{character},NULL);");
            sql.Append($"INSERT INTO marriage_proposal_participants VALUES('{id}',0,{character},{active}),('{id}',1,{character + 1},{active});");
            foreach (var (ceremony, ceremonyState, phase, completedAt, love) in ceremonies)
            {
                sql.Append($"INSERT INTO marriage_ceremonies VALUES('{ceremony}','{id}',{character + 1},'{{}}',0,0,{ceremonyState},1,0,0,NULL);");
                if (phase >= 0)
                    sql.Append($"INSERT INTO marriage_weddings VALUES('{ceremony}','{id}','{{}}',{phase},500000,{completedAt},{love});");
            }
            character += 2;
        }
        return sql.ToString();
    }

    private static string[] Relations(SqliteCommand q) => Rows(q,
        "SELECT id||'|'||stage||'|'||IFNULL(ceremony_id,'')||'|'||IFNULL(married_at,'')||'|'||revision FROM marriage_relations ORDER BY id");

    private static string[] Legacy(SqliteCommand q) =>
    [
        .. Rows(q, "SELECT id||payload_json||state||revision||created_at||IFNULL(response,'-') FROM marriage_proposals ORDER BY id"),
        .. Rows(q, "SELECT proposal_id||role||character_id||active FROM marriage_proposal_participants ORDER BY proposal_id,role"),
        .. Rows(q, "SELECT id||relation_id||state||revision||IFNULL(response,'-') FROM marriage_ceremonies ORDER BY id"),
        .. Rows(q, "SELECT ceremony_id||relation_id||phase||paid_gold||completed_at FROM marriage_weddings ORDER BY ceremony_id"),
    ];

    private static string[] Rows(SqliteCommand q, string sql)
    {
        q.CommandText = sql;
        using var reader = q.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) rows.Add(reader.GetString(0));
        return [.. rows];
    }
}
