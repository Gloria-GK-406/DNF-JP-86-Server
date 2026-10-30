using Dfo.Server.Persistence.Schema;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class PremiumUsageTimestampSchemaTests
{
    [Fact]
    public void Legacy_table_gains_nullable_timestamp_without_losing_other_counters()
    {
        var path = Path.Combine(Path.GetTempPath(), $"premium-timestamp-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new SqliteConnection($"Data Source={path};Pooling=False");
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "CREATE TABLE character_daily_counters(character_id INTEGER,counter_key TEXT,period TEXT,value INTEGER,PRIMARY KEY(character_id,counter_key)); INSERT INTO character_daily_counters VALUES(1,'other_claim','2026-09-22',3);";
            command.ExecuteNonQuery();
            HandWrittenShape.Adopt(path);
            HandWrittenShape.Adopt(path);
            CharacterDatabaseSchema.Ensure(db);
            command.CommandText = "SELECT COUNT(*) FROM character_daily_counters WHERE character_id=1 AND counter_key='other_claim' AND period='2026-09-22' AND value=3 AND last_consumed_at IS NULL";
            Assert.Equal(1L, command.ExecuteScalar());
        }
        finally { File.Delete(path); }
    }
}
