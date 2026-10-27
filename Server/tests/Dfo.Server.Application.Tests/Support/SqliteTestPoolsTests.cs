using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests;

public sealed class SqliteTestPoolsTests
{
    [Fact]
    public void Clearing_an_owned_database_releases_both_its_idle_pools_without_touching_another_database()
    {
        var directory = Path.Combine(Path.GetTempPath(), "owned-pools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var a = Path.Combine(directory, "a.db");
        var b = Path.Combine(directory, "b.db");
        // Use the actual persistence string shapes, independently of the cleanup helper.
        string ProductionWrite(string path) => new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path) }.ToString();
        string ProductionRead(string path) => new SqliteConnectionStringBuilder(ProductionWrite(path)) { Mode = SqliteOpenMode.ReadOnly }.ToString();
        SQLitePCL.sqlite3 Idle(string connectionString)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            Assert.Equal(1L, command.ExecuteScalar());
            return connection.Handle!;
        }
        try
        {
            var aw = Idle(ProductionWrite(a));
            var ar = Idle(ProductionRead(a));
            var bw = Idle(ProductionWrite(b));
            var br = Idle(ProductionRead(b));
            Assert.All(new[] { aw, ar, bw, br }, handle => Assert.False(handle.IsClosed));

            SqliteTestPools.ClearOwned(Path.GetRelativePath(Environment.CurrentDirectory, a));

            Assert.True(aw.IsClosed);
            Assert.True(ar.IsClosed);
            Assert.False(bw.IsClosed);
            Assert.False(br.IsClosed);
            Assert.False(Idle(ProductionWrite(b)).IsClosed);
            Assert.False(Idle(ProductionRead(b)).IsClosed);
        }
        finally
        {
            SqliteTestPools.ClearOwned(a, b);
            Directory.Delete(directory, recursive: true);
        }
    }
}
