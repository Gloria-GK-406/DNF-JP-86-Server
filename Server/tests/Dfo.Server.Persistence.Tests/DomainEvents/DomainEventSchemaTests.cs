using Dfo.Server.Testing;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.DomainEvents;

public sealed class DomainEventSchemaTests
{
    [Fact]
    public void Event_schema_is_idempotent_and_has_due_and_coalescing_indexes()
    {
        var path = Path.Combine(Path.GetTempPath(), "dfo-event-schema-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            _ = new SqliteDatabase(path, TestRequired.TransactionScope());
            _ = new SqliteDatabase(path, TestRequired.TransactionScope());
            using var connection = new SqliteConnection("Data Source=" + path); connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN ('ix_domain_delivery_due','ix_domain_delivery_coalescing','ix_domain_event_batch')";
            Assert.Equal(3L, Convert.ToInt64(command.ExecuteScalar()));
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('domain_events','domain_event_deliveries')";
            Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
        }
        finally
        {
            using var connection = new SqliteConnection("Data Source=" + path); SqliteConnection.ClearPool(connection);
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }
}
