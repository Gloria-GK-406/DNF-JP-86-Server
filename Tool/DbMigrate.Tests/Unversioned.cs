using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

/// <summary>Databases in the state a server from before schema versions left them.</summary>
internal static class Unversioned
{
    public static string NewPath(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");

    /// <summary>
    /// Builds the current shape and removes its stamp. Older servers stamped nothing, and every
    /// table they knew is one the current shape has; what a test needs older than that it alters
    /// afterwards.
    /// </summary>
    public static void CreateUnstamped(string path)
    {
        _ = new SqliteDatabase(path, new DomainTransactionScope());
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version=0;";
        command.ExecuteNonQuery();
    }

    public static void Migrate(string path) => Migrator.Migrate(path, backup: false);

    /// <summary>Opens the database the way the server does, which refuses one in another shape.</summary>
    public static SqliteDatabase Open(string path) => new(path, new DomainTransactionScope());
}
