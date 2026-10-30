namespace Dfo.Server.Persistence.Tests.Support;

/// <summary>
/// A database whose tables a case wrote itself, usually so it can store a row the declared shape
/// forbids.
/// </summary>
/// <remarks>
/// The server only opens a database at the current schema version. Such a database is brought
/// there the way an older one is, by the migration tool, which adds the tables and columns the
/// case left out and stamps the version, while keeping what the case wrote.
/// </remarks>
internal static class HandWrittenShape
{
    public static void Adopt(string path) => DbMigrate.Migrator.Migrate(path, backup: false);
}
