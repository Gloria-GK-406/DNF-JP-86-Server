using DbMigrate.Legacy;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate;

/// <summary>Takes a database from the schema version it is at to the one the server needs.</summary>
/// <remarks>
/// Each step takes one version to the next and is listed under the version it starts from. A step
/// may run in several transactions of its own, so a database is backed up before the first one; a
/// step that fails leaves the version where it was, and the backup is what to go back to.
/// </remarks>
public static class Migrator
{
    private static readonly IReadOnlyDictionary<long, Action<SqliteConnection>> Steps =
        new Dictionary<long, Action<SqliteConnection>>
        {
            [0] = LegacyUpgrade.Apply,
        };

    /// <summary>The outcome of one run, for the caller to report.</summary>
    public sealed record Result(long From, long To, string? Backup);

    public static Result Migrate(string databasePath, bool backup = true)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("There is no database to migrate.", databasePath);

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadWrite }.ToString());
        connection.Open();
        var from = CharacterDatabaseSchema.VersionOf(connection);
        if (from == CharacterDatabaseSchema.Version)
            return new(from, from, null);
        if (from > CharacterDatabaseSchema.Version)
            throw new InvalidOperationException(
                $"The database is at schema version {from}, newer than the {CharacterDatabaseSchema.Version} this tool knows.");
        if (from == 0 && Shape.IsEmpty(connection))
            throw new InvalidOperationException("The database has no tables; the server builds a new one itself.");

        var copy = backup ? Backup(connection, databasePath, from) : null;
        for (var version = from; version < CharacterDatabaseSchema.Version; version++)
        {
            if (!Steps.TryGetValue(version, out var step))
                throw new InvalidOperationException($"No step takes a database from schema version {version}.");
            step(connection);
            CharacterDatabaseSchema.Stamp(connection, null, checked((int)version + 1));
        }

        return new(from, CharacterDatabaseSchema.Version, copy);
    }

    /// <summary>A consistent copy beside the database, taken before anything changes.</summary>
    private static string Backup(SqliteConnection connection, string databasePath, long version)
    {
        var copy = $"{Path.GetFullPath(databasePath)}.v{version}-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO @copy;";
        command.Parameters.AddWithValue("@copy", copy);
        command.ExecuteNonQuery();
        return copy;
    }
}
