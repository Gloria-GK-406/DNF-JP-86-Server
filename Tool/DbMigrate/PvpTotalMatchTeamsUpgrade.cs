using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate;

internal static class PvpTotalMatchTeamsUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = CharacterDatabaseSchema.PvpTotalMatchTeamsDeclaration;
        command.ExecuteNonQuery();
    }
}
