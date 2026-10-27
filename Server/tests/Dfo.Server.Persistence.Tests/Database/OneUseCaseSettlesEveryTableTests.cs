using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Database;

/// <summary>
/// What happens to a use case's changes when part of it fails.
/// </summary>
/// <remarks>
/// Once repositories only speak about one table each, an ordinary business operation is several of
/// them. That is only safe if the several settle as one: a character created without its skills,
/// or a refill that took the count but never delivered the items, is worse than one that never
/// happened.
///
/// This is the guarantee the split spends, so it is asserted before the split is made.
/// </remarks>
public sealed class OneUseCaseSettlesEveryTableTests : IAsyncDisposable
{
    private readonly string path = Path.Combine(
        Path.GetTempPath(), $"dfo-one-usecase-{Guid.NewGuid():N}.db");

    private readonly string connectionString;

    private readonly SqliteDatabase database;

    private readonly DomainTransactionScope scope = new();

    public OneUseCaseSettlesEveryTableTests()
    {
        this.connectionString =
            new SqliteConnectionStringBuilder { DataSource = this.path }.ToString();
        CharacterDatabaseSchema.Ensure(this.connectionString);
        this.Execute(
            "INSERT INTO characters(character_id,account_id,name,level) VALUES(1,1,'settler',10)");
        this.Execute(
            "INSERT INTO character_stats(character_id,gold_carry_limit,auction_gold_limit) "
            + "VALUES(1,700,800)");
        this.database = new SqliteDatabase(this.path, this.scope);
    }

    [Fact]
    public async Task TwoTablesChangedByOneUseCaseBecomeVisibleTogether()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using (var lease = this.scope.Begin())
            {
                await this.WriteAsync("UPDATE characters SET level=42 WHERE character_id=1");
                await this.WriteAsync(
                    "UPDATE character_stats SET gold_carry_limit=999 WHERE character_id=1");
                await lease.CommitAsync(CancellationToken.None);
            }

            Assert.Equal(42L, this.Scalar("SELECT level FROM characters WHERE character_id=1"));
            Assert.Equal(
                999L, this.Scalar("SELECT gold_carry_limit FROM character_stats WHERE character_id=1"));
        });
    }

    /// <remarks>
    /// The second table is named wrongly on purpose. What matters is not which statement failed
    /// but that the first one, which had already succeeded, does not survive on its own.
    /// </remarks>
    [Fact]
    public async Task NeitherTableKeepsItsChangeWhenPartOfTheUseCaseFails()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await using var lease = this.scope.Begin();
                await this.WriteAsync("UPDATE characters SET level=42 WHERE character_id=1");
                await this.WriteAsync("UPDATE character_stats SET nothing=1 WHERE character_id=1");
                await lease.CommitAsync(CancellationToken.None);
            });

            Assert.Equal(10L, this.Scalar("SELECT level FROM characters WHERE character_id=1"));
            Assert.Equal(
                700L, this.Scalar("SELECT gold_carry_limit FROM character_stats WHERE character_id=1"));
        });
    }

    /// <remarks>
    /// Leaving without committing is the ordinary way a use case ends when the business says no,
    /// and it has to take the changes with it just as a failure does.
    /// </remarks>
    [Fact]
    public async Task AUseCaseThatEndsWithoutCommittingKeepsNothing()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using (var lease = this.scope.Begin())
            {
                await this.WriteAsync("UPDATE characters SET level=42 WHERE character_id=1");
                await this.WriteAsync(
                    "UPDATE character_stats SET gold_carry_limit=999 WHERE character_id=1");
                _ = lease;
            }

            Assert.Equal(10L, this.Scalar("SELECT level FROM characters WHERE character_id=1"));
            Assert.Equal(
                700L, this.Scalar("SELECT gold_carry_limit FROM character_stats WHERE character_id=1"));
        });
    }

    private ValueTask WriteAsync(string sql) => this.database.WriteAsync(
        async (command, token) =>
        {
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(token);
        },
        CancellationToken.None);

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(this.connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection(this.connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(this.path + suffix);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }
        }

        return ValueTask.CompletedTask;
    }
}
