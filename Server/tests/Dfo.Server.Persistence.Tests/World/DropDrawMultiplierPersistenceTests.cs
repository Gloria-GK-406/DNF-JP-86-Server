using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.World;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.World;

public sealed class DropDrawMultiplierPersistenceTests
{
    [Fact]
    public async Task A_saved_multiplier_survives_a_reopen_and_can_be_overwritten()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "dfo-drop-draw-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "test.db");
            async Task SaveAsync(int multiplier)
            {
                var scope = TestRequired.TransactionScope();
                var store = new SqliteDropDrawMultiplierPersistence(new SqliteDatabase(path, scope));
                await using var transaction = scope.Begin();
                await store.SaveAsync("world", multiplier, default);
                await transaction.CommitAsync(default);
            }
            ValueTask<IReadOnlyList<(string Key, int Multiplier)>> ReadAsync() =>
                new SqliteDropDrawMultiplierPersistence(new SqliteDatabase(path, TestRequired.TransactionScope()))
                    .ReadAllAsync(default);

            await SaveAsync(20);
            Assert.Equal(("world", 20), Assert.Single(await ReadAsync()));

            await SaveAsync(3);
            Assert.Equal(("world", 3), Assert.Single(await ReadAsync()));

            await Assert.ThrowsAsync<SqliteException>(async () => await SaveAsync(101));

            using var connection = new SqliteConnection($"Data Source={path}");
            SqliteConnection.ClearPool(connection);
            Directory.Delete(directory, true);
        });
    }
}
