using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Schema;

internal static class MarriageHousingMigration
{
    public static void Apply(SqliteConnection connection)
    {
        using var tx = connection.BeginTransaction();
        using var q = connection.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT COUNT(*) FROM marriage_housing_migrations WHERE version=1";
        if ((long)q.ExecuteScalar()! != 0) { tx.Commit(); return; }
        // Roles provide a deterministic remainder rule for old odd balances; total points are conserved.
        q.CommandText = """
            INSERT INTO marriage_points_migration_audit
                SELECT id,love_points,love_points/2,love_points-love_points/2 FROM marriage_relations WHERE stage=4;
            INSERT INTO marriage_personal_points(relation_id,character_id,points)
                SELECT r.id,p.character_id,MIN(999,CASE p.role WHEN 0 THEN r.love_points/2 ELSE r.love_points-r.love_points/2 END)
                FROM marriage_relations r JOIN marriage_proposal_participants p ON p.proposal_id=r.id WHERE r.stage=4;
            INSERT INTO marriage_housing_rooms(relation_id) SELECT id FROM marriage_relations WHERE stage=4;
            INSERT INTO marriage_housing_migrations VALUES(1,@now);
            """;
        q.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        q.ExecuteNonQuery(); tx.Commit();
    }
}
