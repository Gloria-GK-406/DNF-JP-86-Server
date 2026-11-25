using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate;

internal static class PvpDailyMissionMetricsUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CharacterDatabaseSchema.PvpDailyMissionMetricsDeclaration;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
