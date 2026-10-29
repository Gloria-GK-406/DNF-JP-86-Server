using DbMigrate;

// Usage: DbMigrate <database-path> [--no-backup]
// Run while the server is stopped. The database is backed up beside itself first.
if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--no-backup")
{
    Console.Error.WriteLine("Usage: DbMigrate <database-path> [--no-backup]");
    return 2;
}

try
{
    var result = Migrator.Migrate(args[0], backup: args.Length == 1);
    if (result.From == result.To)
    {
        Console.WriteLine($"Already at schema version {result.To}; nothing to do.");
        return 0;
    }

    if (result.Backup is not null)
        Console.WriteLine($"Backed up to {result.Backup}");
    Console.WriteLine($"Migrated from schema version {result.From} to {result.To}.");
    return 0;
}
catch (Exception error) when (error is InvalidOperationException or InvalidDataException or FileNotFoundException
                                   or Microsoft.Data.Sqlite.SqliteException)
{
    Console.Error.WriteLine($"Migration failed: {error.Message}");
    return 1;
}
