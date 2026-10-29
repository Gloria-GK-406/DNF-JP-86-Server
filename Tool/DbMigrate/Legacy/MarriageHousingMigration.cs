using Microsoft.Data.Sqlite;

namespace DbMigrate.Legacy;

internal static class MarriageHousingMigration
{
    public static void Apply(SqliteConnection connection)
    {
        using var tx = connection.BeginTransaction();
        using var q = connection.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT COUNT(*) FROM marriage_housing_migrations WHERE version=1";
        if ((long)q.ExecuteScalar()! != 0) { tx.Commit(); return; }
        // A couple's shared points were the relation's own column, kept at least the completed wedding's; a database
        // from before relations has only the wedding's. Points only ever grew, so the larger is the balance.
        // Roles provide a deterministic remainder rule for old odd balances; total points are conserved.
        string[] sources =
        [
            .. Shape.HasColumn(connection, tx, "marriage_relations", "love_points") ? ["r.love_points"] : Array.Empty<string>(),
            .. Shape.HasColumn(connection, tx, "marriage_weddings", "love_points")
                ? ["COALESCE((SELECT MAX(w.love_points) FROM marriage_weddings w WHERE w.relation_id=r.id AND w.phase=1),0)"]
                : Array.Empty<string>(),
        ];
        var shared = sources.Length switch { 0 => "0", 1 => sources[0], _ => $"MAX({string.Join(',', sources)})" };
        q.CommandText = $"""
            CREATE TEMP TABLE shared_points AS SELECT r.id AS relation_id,{shared} AS points FROM marriage_relations r WHERE r.stage=4;
            INSERT INTO marriage_points_migration_audit
                SELECT relation_id,points,points/2,points-points/2 FROM shared_points;
            INSERT INTO marriage_personal_points(relation_id,character_id,points)
                SELECT s.relation_id,p.character_id,MIN(999,CASE p.role WHEN 0 THEN s.points/2 ELSE s.points-s.points/2 END)
                FROM shared_points s JOIN marriage_proposal_participants p ON p.proposal_id=s.relation_id;
            DROP TABLE shared_points;
            INSERT INTO marriage_housing_rooms(relation_id) SELECT id FROM marriage_relations WHERE stage=4;
            INSERT INTO marriage_housing_migrations VALUES(1,@now);
            """;
        q.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        q.ExecuteNonQuery(); tx.Commit();
    }
}
