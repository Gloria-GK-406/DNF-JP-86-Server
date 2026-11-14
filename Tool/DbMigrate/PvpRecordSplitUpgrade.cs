using Microsoft.Data.Sqlite;

namespace DbMigrate;

/// <summary>
/// Splits <c>character_pvp</c> into the duel record, the duel skill state and an audit of the
/// columns nobody could name, and adds the battle tables (schema 10 to 11).
/// </summary>
/// <remarks>
/// The statements are copied from the version-11 declaration rather than read from it: this step
/// must keep producing the version-11 shape after the declaration moves on.
///
/// Every <c>character_pvp</c> row becomes one record row and one skill-state row, so grade, rating
/// grade and whether the duel skill pages were seeded are exactly what they were. The record starts
/// with no wins, losses, draws or streaks and with no grade experience: a null reads as the lower
/// bound of the grade, which needs the archive this tool does not open. A character that had no
/// <c>character_pvp</c> row is not given one, so a character that could not be entered before still
/// cannot, rather than entering with an invented grade.
///
/// <c>pvp_rank_point</c>, <c>pvp_stat_a</c>, <c>pvp_win_streak</c> and <c>pvp_lose_streak</c> were
/// never sent anywhere and their meaning is not known, so they go to <c>character_pvp_legacy</c>
/// as they were, for the rows where any of them is not zero, and nothing reads them.
///
/// A database that came through the legacy step already has the new tables, built from the current
/// declaration, and may still have <c>character_pvp</c>; one that never had that table has nothing
/// to copy. Both pass through.
/// </remarks>
internal static class PvpRecordSplitUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS character_pvp_record ( character_id INTEGER PRIMARY KEY, grade INTEGER NOT NULL DEFAULT 0, rating_grade INTEGER NOT NULL DEFAULT 0, grade_experience INTEGER CHECK (grade_experience >= 0), win INTEGER NOT NULL DEFAULT 0 CHECK (win >= 0), lose INTEGER NOT NULL DEFAULT 0 CHECK (lose >= 0), draw INTEGER NOT NULL DEFAULT 0 CHECK (draw >= 0), win_streak INTEGER NOT NULL DEFAULT 0 CHECK (win_streak >= 0), peak_win_streak INTEGER NOT NULL DEFAULT 0 CHECK (peak_win_streak >= win_streak), FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE );
            CREATE TABLE IF NOT EXISTS character_pvp_skill_state ( character_id INTEGER PRIMARY KEY, skills_initialized INTEGER NOT NULL DEFAULT 0, FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE );
            CREATE TABLE IF NOT EXISTS character_pvp_legacy ( character_id INTEGER PRIMARY KEY, rank_point INTEGER NOT NULL, stat_a INTEGER NOT NULL, win_streak INTEGER NOT NULL, lose_streak INTEGER NOT NULL );
            CREATE TABLE IF NOT EXISTS pvp_matches ( match_id TEXT PRIMARY KEY, room_id INTEGER NOT NULL, listener_port INTEGER NOT NULL, channel_env INTEGER, battle_mode INTEGER NOT NULL, outcome_kind INTEGER NOT NULL CHECK (outcome_kind IN (0, 1, 2)), winner INTEGER, records_result INTEGER NOT NULL CHECK (records_result IN (0, 1)), game_day TEXT NOT NULL, completed_at INTEGER NOT NULL, CHECK (outcome_kind <> 2 OR records_result = 0) );
            CREATE TABLE IF NOT EXISTS pvp_match_results ( match_id TEXT NOT NULL REFERENCES pvp_matches(match_id), character_id INTEGER NOT NULL, seat INTEGER NOT NULL, team INTEGER NOT NULL, outcome INTEGER NOT NULL CHECK (outcome IN (0, 1, 2)), kills INTEGER NOT NULL CHECK (kills >= 0), deaths INTEGER NOT NULL CHECK (deaths >= 0), fled INTEGER NOT NULL CHECK (fled IN (0, 1)), counts_toward_daily INTEGER NOT NULL CHECK (counts_toward_daily IN (0, 1)), previous_grade INTEGER NOT NULL, grade INTEGER NOT NULL, experience_change INTEGER NOT NULL, experience INTEGER NOT NULL CHECK (experience >= 0), win_streak INTEGER NOT NULL CHECK (win_streak >= 0), PRIMARY KEY (match_id, character_id), CHECK (fled = 0 OR outcome = 1) );
            CREATE INDEX IF NOT EXISTS ix_pvp_match_results_character ON pvp_match_results(character_id);
            """);

        if (Shape.HasTable(connection, transaction, "character_pvp"))
        {
            Execute(connection, transaction, """
                INSERT INTO character_pvp_record(character_id,grade,rating_grade)
                    SELECT character_id,pvp_grade,pvp_rating_grade FROM character_pvp;
                INSERT INTO character_pvp_skill_state(character_id,skills_initialized)
                    SELECT character_id,skills_initialized FROM character_pvp;
                INSERT INTO character_pvp_legacy(character_id,rank_point,stat_a,win_streak,lose_streak)
                    SELECT character_id,pvp_rank_point,pvp_stat_a,pvp_win_streak,pvp_lose_streak FROM character_pvp
                    WHERE pvp_rank_point<>0 OR pvp_stat_a<>0 OR pvp_win_streak<>0 OR pvp_lose_streak<>0;
                DROP TABLE character_pvp;
                """);
        }

        transaction.Commit();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
