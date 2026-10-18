using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
namespace Dfo.Server.Persistence.Tests.Schema;
public sealed class MarriageWeddingMigrationTests
{
    [Fact]
    public void Old_wedding_rows_keep_their_identity_and_acquire_nullable_history_without_inventing_a_date()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wedding-migration-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new SqliteConnection($"Data Source={path}"); db.Open(); using var q = db.CreateCommand();
            q.CommandText = "CREATE TABLE marriage_weddings(ceremony_id TEXT PRIMARY KEY,relation_id TEXT NOT NULL,payload_json TEXT NOT NULL,phase INTEGER NOT NULL,paid_gold INTEGER NOT NULL); INSERT INTO marriage_weddings VALUES('legacy','relation','{}',1,500000)"; q.ExecuteNonQuery();
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            q.CommandText = "SELECT completed_at||':'||love_points||':'||paid_gold FROM marriage_weddings WHERE ceremony_id='legacy'";
            Assert.Equal("0:0:500000", q.ExecuteScalar());
            q.CommandText = "UPDATE marriage_weddings SET completed_at=1792206195530,love_points=7"; q.ExecuteNonQuery();
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            q.CommandText = "SELECT completed_at||':'||love_points FROM marriage_weddings WHERE ceremony_id='legacy'";
            Assert.Equal("1792206195530:7", q.ExecuteScalar());
            q.CommandText = "UPDATE marriage_weddings SET love_points=-1"; Assert.Throws<SqliteException>(() => q.ExecuteNonQuery());
            q.CommandText = "PRAGMA integrity_check"; Assert.Equal("ok", q.ExecuteScalar());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
