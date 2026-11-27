using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate;

// Unreleased guild builds used 14..40 while main assigned 14..21 to PvP.
// Keep the original guild position across failures while replaying the main PvP steps.
internal static class GuildBranchUpgrade
{
    private const string ResumeTable = "migration_guild_branch_resume";

    public static long BeginIfNeeded(SqliteConnection connection, long version)
    {
        if (version is < 14 or > 40 || !Shape.HasTable(connection, null, "guilds")
            || !Shape.HasTable(connection, null, "character_pvp"))
            return version;

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"CREATE TABLE {ResumeTable}(guild_version INTEGER NOT NULL CHECK(guild_version BETWEEN 14 AND 40));"
            + $"INSERT INTO {ResumeTable}(guild_version) VALUES (@version);";
        command.Parameters.AddWithValue("@version", version);
        command.ExecuteNonQuery();
        CharacterDatabaseSchema.Stamp(connection, transaction, 13);
        transaction.Commit();
        return 13;
    }

    public static long ResumeGuildSteps(SqliteConnection connection)
    {
        if (!Shape.HasTable(connection, null, ResumeTable))
            return 21;

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT guild_version FROM {ResumeTable};";
        var next = checked((int)(long)command.ExecuteScalar()! + 8);
        command.CommandText = $"DROP TABLE {ResumeTable};";
        command.ExecuteNonQuery();
        CharacterDatabaseSchema.Stamp(connection, transaction, next);
        transaction.Commit();
        return next;
    }
}
