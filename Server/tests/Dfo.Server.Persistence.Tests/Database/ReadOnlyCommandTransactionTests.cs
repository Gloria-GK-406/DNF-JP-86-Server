using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Persistence.Tests.Database;

public sealed class ReadOnlyCommandTransactionTests : IAsyncDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"dfo-readonly-command-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider provider;
    private readonly SqliteDatabase database;

    public ReadOnlyCommandTransactionTests()
    {
        var services = new ServiceCollection().AddLogging().AddCommandDispatching();
        services.AddSingleton(sp => new SqliteDatabase(path, sp.GetRequiredService<IDomainTransactionAccessor>()));
        services.AddCommandHandler<QueryCommand, long, QueryHandler>(CommandExecutionKind.ReadOnly);
        provider = services.BuildServiceProvider();
        database = provider.GetRequiredService<SqliteDatabase>();
        Execute("CREATE TABLE read_only_probe(id INTEGER PRIMARY KEY, value INTEGER NOT NULL); "
            + "INSERT INTO read_only_probe VALUES(1,10),(2,20)");
    }

    [Fact]
    public async Task Query_reads_one_snapshot_even_when_another_writer_commits_between_statements()
    {
        var result = await Query(async (db, _, ct) =>
        {
            Assert.Equal(10, await ReadValue(db, 1, ct));
            Execute("UPDATE read_only_probe SET value=99 WHERE id=2");
            return await ReadValue(db, 2, ct);
        });

        Assert.Equal(20, result);
        Assert.Equal(99, Scalar("SELECT value FROM read_only_probe WHERE id=2"));
        Assert.Equal(99, await Query((db, _, ct) => ReadValue(db, 2, ct)));
    }

    [Theory]
    [InlineData("write")]
    [InlineData("returning")]
    [InlineData("disguised-read")]
    public async Task Query_refuses_database_edits_and_keeps_saved_values_unchanged(string pathKind)
    {
        var failure = await Record.ExceptionAsync(async () => await Query(async (db, _, ct) =>
        {
            switch (pathKind)
            {
                case "write":
                    await db.WriteAsync(async (command, token) =>
                    {
                        command.CommandText = "UPDATE read_only_probe SET value=99 WHERE id=1";
                        await command.ExecuteNonQueryAsync(token);
                    }, ct);
                    break;
                case "returning":
                    await db.WriteReturningAsync("UPDATE read_only_probe SET value=99 WHERE id=1 RETURNING value",
                        _ => { }, reader => new SavedValue(reader.GetInt64(0)), ct);
                    break;
                case "disguised-read":
                    await db.ReadFirstAsync("UPDATE read_only_probe SET value=99 WHERE id=1 RETURNING value",
                        _ => { }, reader => new SavedValue(reader.GetInt64(0)), ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pathKind));
            }
            return await ReadValue(db, 1, ct);
        }));

        if (pathKind == "disguised-read")
            Assert.Equal(8, Assert.IsType<SqliteException>(failure).SqliteErrorCode);
        else
            Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(10, Scalar("SELECT value FROM read_only_probe WHERE id=1"));
        Assert.Equal(10, await Query((db, _, ct) => ReadValue(db, 1, ct)));
    }

    [Fact]
    public async Task Query_cannot_apply_a_writable_memory_participant()
    {
        var state = new MemoryState();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Query((_, accessor, _) =>
        {
            accessor.Current!.Enlist("memory", () => new MemoryChange(state));
            return ValueTask.FromResult(0L);
        }));

        Assert.Equal(0, state.Value);
        Assert.Equal(10, await Query((db, _, ct) => ReadValue(db, 1, ct)));
    }

    [Fact]
    public async Task Query_cannot_schedule_an_effect_after_commit()
    {
        var effects = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Query(async (db, accessor, ct) =>
        {
            var value = await ReadValue(db, 1, ct);
            accessor.Current!.RegisterAfterCommit("effect", _ =>
            {
                effects++;
                return ValueTask.CompletedTask;
            });
            return value;
        }));

        Assert.Equal(0, effects);
        Assert.Equal(10, Scalar("SELECT value FROM read_only_probe WHERE id=1"));
        Assert.Equal(10, await Query((db, _, ct) => ReadValue(db, 1, ct)));
        Assert.Equal(0, effects);
    }

    private ValueTask<long> Query(Func<SqliteDatabase, IDomainTransactionAccessor, CancellationToken, ValueTask<long>> execute) =>
        provider.GetRequiredService<ICommandBus>().SendAsync(new QueryCommand(execute));

    private static ValueTask<long> ReadValue(SqliteDatabase db, int id, CancellationToken ct) =>
        db.ReadAsync(async (command, token) =>
        {
            command.CommandText = "SELECT value FROM read_only_probe WHERE id=$id";
            command.Parameters.AddWithValue("$id", id);
            return Convert.ToInt64(await command.ExecuteScalarAsync(token));
        }, ct);

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection(database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private sealed record SavedValue(long Value);
    private sealed record QueryCommand(Func<SqliteDatabase, IDomainTransactionAccessor, CancellationToken, ValueTask<long>> Execute)
        : ICommand<long>;
    private sealed class QueryHandler(SqliteDatabase db, IDomainTransactionAccessor transactions)
        : ICommandHandler<QueryCommand, long>
    {
        public ValueTask<long> HandleAsync(CommandExecutionContext context, QueryCommand command, CancellationToken ct) =>
            command.Execute(db, transactions, ct);
    }
    private sealed class MemoryState { public int Value { get; set; } }
    private sealed class MemoryChange(MemoryState state) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) { state.Value++; return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(path + suffix);
    }
}
