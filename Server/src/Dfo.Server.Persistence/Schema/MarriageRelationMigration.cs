using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Schema;

/// <summary>Gives every proposal that has no relation row the stage its proposal, ceremony and wedding rows imply.</summary>
/// <remarks>
/// Rows that already exist are never touched, so the stage they reached at runtime survives a restart.
/// Nothing in the old rows says when a stage began, so every backfilled row starts at the migration instant.
/// An Engaged proposal still holding a Reserved ceremony without a live wedding stays Engaged; the relation
/// store counts those for the startup audit rather than guessing what they should be.
/// <para>
/// Stages get no reconciling pass on later startups: a lower stage can be a legitimate cancellation, so a rollback that
/// advanced marriages in the old tables is restored from the matching backup instead. Love points are not carried
/// here; <see cref="MarriageHousingMigration"/> reads the old columns straight into personal points.
/// </para>
/// </remarks>
internal static class MarriageRelationMigration
{
    public static void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO marriage_relations(id,stage,revision,stage_since,ceremony_id,married_at)
            SELECT p.id,
                CASE WHEN p.state=0 THEN 0 WHEN p.state<>1 THEN 5
                     WHEN m.ceremony_id IS NOT NULL THEN 4 WHEN w.ceremony_id IS NOT NULL THEN 3
                     WHEN c.id IS NOT NULL THEN 2 ELSE 1 END,
                1, @now,
                CASE WHEN p.state=1 THEN COALESCE(m.ceremony_id,w.ceremony_id,c.id) END,
                CASE WHEN p.state=1 AND m.completed_at>0 THEN m.completed_at END
            FROM marriage_proposals p
            LEFT JOIN marriage_weddings m ON m.relation_id=p.id AND m.phase=1
            LEFT JOIN marriage_weddings w ON w.relation_id=p.id AND w.phase=0
            LEFT JOIN marriage_ceremonies c ON c.relation_id=p.id AND c.state=0;
            """;
        command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }
}
