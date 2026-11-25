using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate;

internal static class PvpTotalMatchRatingResultsUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CharacterDatabaseSchema.PvpTotalMatchRatingResultsDeclaration
            + "\n" + CharacterDatabaseSchema.PvpTotalMatchRatingResultsIndexDeclaration;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
