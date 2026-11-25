using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate;

internal static class PvpMatchRatingResultsUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CharacterDatabaseSchema.PvpMatchRatingResultsDeclaration
            + "\n" + CharacterDatabaseSchema.PvpMatchRatingResultsIndexDeclaration;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
