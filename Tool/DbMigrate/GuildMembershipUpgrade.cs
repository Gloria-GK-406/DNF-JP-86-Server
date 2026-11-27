using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate;

internal static class GuildMembershipUpgrade
{
    public static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (!Shape.HasColumn(connection, transaction, "guild_members", "membership_id"))
        {
            // Preserve the roster without firing departure logic for a single member.
            command.CommandText = """
                CREATE TEMP TABLE saved_guild_members AS
                    SELECT character_id,guild_id,is_leader,joined_at,donation_points,member_rank,memo FROM guild_members;
                DROP TRIGGER IF EXISTS guild_red_packets_mark_departure;
                DROP TABLE guild_members;
                """;
            command.ExecuteNonQuery();
            CharacterDatabaseSchema.DeclareGuilds(connection, transaction);
            command.CommandText = """
                INSERT INTO guild_members(character_id,guild_id,is_leader,joined_at,donation_points,member_rank,memo)
                    SELECT character_id,guild_id,is_leader,joined_at,donation_points,member_rank,memo
                    FROM saved_guild_members ORDER BY character_id;
                DROP TABLE saved_guild_members;
                """;
            command.ExecuteNonQuery();
            CharacterDatabaseSchema.DeclareGuildRedPackets(connection, transaction);
        }

        command.CommandText = "DROP VIEW IF EXISTS guild_contribution_entries";
        command.ExecuteNonQuery();
        long ties = 0;
        foreach (var (ledger, character, occurred) in new[]
        {
            ("guild_gold_donations", "donor_character_id", "donated_at"),
            ("guild_monthly_contribution_events", "character_id", "earned_at"),
            ("guild_daily_contributions", "character_id", "earned_at"),
            ("guild_clear_contributions", "character_id", "earned_at"),
        })
        {
            if (!Shape.HasColumn(connection, transaction, ledger, "membership_id"))
            {
                command.CommandText = $"ALTER TABLE {ledger} ADD COLUMN membership_id INTEGER";
                command.ExecuteNonQuery();
            }
            // Freeze the pre-upgrade attribution once. Unmatched history still contributes
            // to guild totals, and cannot be acquired by a future membership.
            var matching = $"""
                FROM guild_members m WHERE e.guild_id=m.guild_id
                    AND e.{character}=m.character_id AND e.membership_id IS NULL
                """;
            command.CommandText = $"SELECT COUNT(*) FROM {ledger} e JOIN guild_members m ON e.guild_id=m.guild_id AND e.{character}=m.character_id WHERE e.membership_id IS NULL AND e.{occurred}=m.joined_at";
            ties += (long)command.ExecuteScalar()!;
            command.CommandText = $"UPDATE {ledger} AS e SET membership_id=m.membership_id {matching} AND e.{occurred}>=m.joined_at";
            command.ExecuteNonQuery();
        }
        CharacterDatabaseSchema.DeclareGuildContributionEntries(connection, transaction);
        transaction.Commit();
        Console.WriteLine($"Guild membership migration retained previous attribution for {ties} contribution rows recorded in the joining second.");
    }
}
