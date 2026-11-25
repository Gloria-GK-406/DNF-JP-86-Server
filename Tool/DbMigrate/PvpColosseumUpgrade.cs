using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate;

internal static class PvpColosseumUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CharacterDatabaseSchema.PvpColosseumDeclaration + "\n" + CharacterDatabaseSchema.PvpColosseumIndex;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
