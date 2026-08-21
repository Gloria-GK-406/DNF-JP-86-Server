using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Database;

/// <summary>
/// What one use case sees while it is reading, and what it may do when there is no use case.
/// </summary>
/// <remarks>
/// A use case that reads several tables is asking one question in several statements. If those
/// statements are not inside one transaction, the answer can change between them: everything read
/// before someone else's commit describes one world and everything after describes another, and
/// nothing in the result says which parts are which.
///
/// This is not hypothetical for a character entering the game. That load reads dozens of tables,
/// and every one of them was its own transaction.
/// </remarks>
public sealed class UseCaseTransactionCoversReadsTests : IAsyncDisposable
{
    private readonly string path = Path.Combine(
        Path.GetTempPath(), $"dfo-usecase-read-{Guid.NewGuid():N}.db");

    private readonly string connectionString;

    public UseCaseTransactionCoversReadsTests()
    {
        this.connectionString =
            new SqliteConnectionStringBuilder { DataSource = this.path }.ToString();
        CharacterDatabaseSchema.Ensure(this.connectionString);
        this.Execute(
            "INSERT INTO characters(character_id,account_id,name,level) VALUES(1,1,'reader',10)");
        this.Execute(
            "INSERT INTO character_gold_limits(character_id,gold_carry_limit,auction_gold_limit) "
            + "VALUES(1,700,800)");
    }

    /// <remarks>
    /// The mode is what makes the rest of this possible, so it is asserted rather than assumed.
    /// Under the default journal a reader holds the file and the writer below would be refused
    /// instead of carrying on.
    /// </remarks>
    [Fact]
    public void TheDatabaseKeepsWritersAndReadersApart()
    {
        Assert.Equal("wal", this.Text("PRAGMA journal_mode"));
    }

    [Fact]
    public async Task AUseCaseReadingTwoTablesDoesNotSeeACommitThatLandsBetweenTheReads()
    {
        var transaction = new DomainTransaction();
        var database = transaction.Enlist(
            this.connectionString, () => new SqliteDatabaseParticipant(this.connectionString));

        var level = await ScalarAsync(database, "SELECT level FROM characters WHERE character_id=1");

        // Someone else's use case finishes entirely while this one is still reading.
        this.Execute("UPDATE character_gold_limits SET gold_carry_limit=999 WHERE character_id=1");

        var limit = await ScalarAsync(
            database, "SELECT gold_carry_limit FROM character_gold_limits WHERE character_id=1");
        await transaction.CommitAsync(CancellationToken.None);

        Assert.Equal(10L, level);
        Assert.Equal(700L, limit);
    }

    /// <summary>A use case that only read still has a transaction, and it has to end.</summary>
    /// <remarks>
    /// Asked of the database rather than of our own objects. The probe is a truncating checkpoint,
    /// which reports itself busy for exactly as long as some connection still holds a view of the
    /// database older than the newest committed change -- which is what a use case's read
    /// transaction is. It reads `busy` while the use case is open and clear once it has ended.
    ///
    /// The obvious probe -- write from outside and see that it succeeds -- proves nothing here:
    /// this database lets writers past readers, so it succeeds either way.
    /// </remarks>
    [Fact]
    public async Task AUseCaseThatOnlyReadLetsGoOfItsViewOfTheDatabase()
    {
        var transaction = new DomainTransaction();
        var database = transaction.Enlist(
            this.connectionString, () => new SqliteDatabaseParticipant(this.connectionString));
        _ = await ScalarAsync(database, "SELECT level FROM characters WHERE character_id=1");
        // Committed after the use case's view was taken, so the view is now the older one.
        this.Execute("UPDATE characters SET level=11 WHERE character_id=1");

        Assert.Equal(1L, this.CheckpointBusy());
        await transaction.CommitAsync(CancellationToken.None);

        Assert.Equal(0L, this.CheckpointBusy());
    }

    /// <summary>And lets go of the connection it was holding that view on.</summary>
    /// <remarks>
    /// Also asked of the database. The file that carries what has been written but not yet folded
    /// back in exists for exactly as long as some connection is open on the database, so once
    /// every pooled handle is really closed, its presence means one connection was never given
    /// back. Settling the transaction is not enough on its own: a participant can commit and still
    /// keep its connection, and nothing else in this class would notice.
    /// </remarks>
    [Fact]
    public async Task AUseCaseThatOnlyReadLetsGoOfItsConnection()
    {
        var transaction = new DomainTransaction();
        var database = transaction.Enlist(
            this.connectionString, () => new SqliteDatabaseParticipant(this.connectionString));
        _ = await ScalarAsync(database, "SELECT level FROM characters WHERE character_id=1");

        await transaction.CommitAsync(CancellationToken.None);

        SqliteConnection.ClearAllPools();
        Assert.False(File.Exists(this.path + "-wal"));
    }

    /// <remarks>
    /// Reading is not a change, so a read that happens outside any use case has nothing to settle
    /// and no outcome to belong to. Refusing it would push a boundary onto every caller that only
    /// wanted to look at something, and there are callers -- a login checking whether an account
    /// exists -- that legitimately have none.
    /// </remarks>
    [Fact]
    public async Task AReadOutsideAnyUseCaseIsAllowed()
    {
        var database = new SqliteDatabase(this.path, new DomainTransactionScope());

        var level = await database.ReadAsync(
            async (command, token) =>
            {
                command.CommandText = "SELECT level FROM characters WHERE character_id=1";
                return Convert.ToInt64(await command.ExecuteScalarAsync(token));
            },
            CancellationToken.None);

        Assert.Equal(10L, level);
    }

    /// <remarks>
    /// The other half of the same rule. A write with no use case around it would look like it
    /// worked and would not be taken back by whatever the caller was in the middle of -- and no
    /// test would show it, because a lone write does succeed.
    /// </remarks>
    [Fact]
    public async Task AWriteOutsideAnyUseCaseIsRefused()
    {
        var database = new SqliteDatabase(this.path, new DomainTransactionScope());

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await database.WriteAsync(
                async (command, token) =>
                {
                    command.CommandText = "UPDATE characters SET level=99 WHERE character_id=1";
                    await command.ExecuteNonQueryAsync(token);
                },
                CancellationToken.None));

        Assert.Contains("use case", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10L, this.Scalar("SELECT level FROM characters WHERE character_id=1"));
    }

    /// <remarks>
    /// The same database reached two ways is one participant, not two. Were it otherwise, a use
    /// case that touched both would be refused outright: a transaction admits one participant
    /// whose commit can still fail.
    /// </remarks>
    [Fact]
    public async Task AReadAndAWriteThroughTheSameDatabaseShareOneUseCase()
    {
        var scope = new DomainTransactionScope();
        var database = new SqliteDatabase(this.path, scope);

        await using (var lease = scope.Begin())
        {
            await database.WriteAsync(
                async (command, token) =>
                {
                    command.CommandText = "UPDATE characters SET level=42 WHERE character_id=1";
                    await command.ExecuteNonQueryAsync(token);
                },
                CancellationToken.None);

            // Its own change, before anyone outside could possibly see it.
            var seen = await database.ReadAsync(
                async (command, token) =>
                {
                    command.CommandText = "SELECT level FROM characters WHERE character_id=1";
                    return Convert.ToInt64(await command.ExecuteScalarAsync(token));
                },
                CancellationToken.None);
            Assert.Equal(42L, seen);
            Assert.Equal(10L, this.Scalar("SELECT level FROM characters WHERE character_id=1"));

            await lease.CommitAsync(CancellationToken.None);
        }

        Assert.Equal(42L, this.Scalar("SELECT level FROM characters WHERE character_id=1"));
    }

    private static async ValueTask<long> ScalarAsync(
        SqliteDatabaseParticipant database, string sql)
    {
        await using var command = await database.CommandAsync(CancellationToken.None);
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None));
    }

    /// <summary>Whether some connection still holds a view older than the newest change.</summary>
    private long CheckpointBusy()
    {
        using var connection = new SqliteConnection(this.connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return reader.GetInt64(0);
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(this.connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private string Text(string sql)
    {
        using var connection = new SqliteConnection(this.connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
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
