using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests;

internal static class SqliteTestPools
{
    public static string ConnectionString(string databasePath, bool readOnly = false)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath) };
        if (readOnly)
            builder.Mode = SqliteOpenMode.ReadOnly;
        return builder.ToString();
    }

    public static void ClearOwned(params string[] databasePaths)
    {
        foreach (var path in databasePaths)
        {
            using var write = new SqliteConnection(ConnectionString(path));
            using var read = new SqliteConnection(ConnectionString(path, readOnly: true));
            SqliteConnection.ClearPool(write);
            SqliteConnection.ClearPool(read);
        }
    }
}
