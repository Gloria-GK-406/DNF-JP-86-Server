using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Schema;

/// <summary>Gives every proposal that has no relation row the stage its proposal, ceremony and wedding rows imply.</summary>
/// <remarks>
/// Rows that already exist are never touched, so the stage they reached at runtime survives a restart.
/// Nothing in the old rows says when a stage began, so every backfilled row starts at the migration instant.
/// An Engaged proposal still holding a Reserved ceremony without a live wedding stays Engaged; the relation
/// store counts those for the startup audit rather than guessing what they should be.
/// <para>
/// Love points are then raised to the completed wedding's old column when that is higher. Points only ever grow and the
/// relation store writes both columns equal, so only a binary that knows nothing of relations can have left the old
/// column ahead. Stages get no such pass: a lower stage can be a legitimate cancellation, so a rollback that advanced
/// marriages in the old tables is restored from the matching backup instead.
/// </para>
/// </remarks>
internal static class MarriageRelationMigration
{
    public static void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO marriage_relations(id,stage,revision,stage_since,ceremony_id,married_at,love_points)
            SELECT p.id,
                CASE WHEN p.state=0 THEN 0 WHEN p.state<>1 THEN 5
                     WHEN m.ceremony_id IS NOT NULL THEN 4 WHEN w.ceremony_id IS NOT NULL THEN 3
                     WHEN c.id IS NOT NULL THEN 2 ELSE 1 END,
                1, @now,
                CASE WHEN p.state=1 THEN COALESCE(m.ceremony_id,w.ceremony_id,c.id) END,
                CASE WHEN p.state=1 AND m.completed_at>0 THEN m.completed_at END,
                CASE WHEN p.state=1 THEN COALESCE(m.love_points,0) ELSE 0 END
            FROM marriage_proposals p
            LEFT JOIN marriage_weddings m ON m.relation_id=p.id AND m.phase=1
            LEFT JOIN marriage_weddings w ON w.relation_id=p.id AND w.phase=0
            LEFT JOIN marriage_ceremonies c ON c.relation_id=p.id AND c.state=0;
            """;
        command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
        command.CommandText = """
            UPDATE marriage_relations
            SET love_points=(SELECT MAX(w.love_points) FROM marriage_weddings w WHERE w.relation_id=marriage_relations.id AND w.phase=1)
            WHERE NOT EXISTS(SELECT 1 FROM marriage_housing_migrations WHERE version=1)
              AND love_points<(SELECT MAX(w.love_points) FROM marriage_weddings w WHERE w.relation_id=marriage_relations.id AND w.phase=1);
            """;
        command.Parameters.Clear();
        command.ExecuteNonQuery();
    }
}
