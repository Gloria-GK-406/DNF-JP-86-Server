using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Database;

public sealed class AfterCommitEffectsTests : IAsyncDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"dfo-after-commit-{Guid.NewGuid():N}.db");
    private readonly DomainTransactionScope scope = new();
    private readonly SqliteDatabase database;

    public AfterCommitEffectsTests()
    {
        database = new SqliteDatabase(path, scope);
        using var connection = new SqliteConnection(database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE after_commit_probe(value INTEGER NOT NULL); INSERT INTO after_commit_probe VALUES(10)";
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task Failed_notification_keeps_durable_commit_and_does_not_skip_the_next_notification()
    {
        var memory = new Participant();
        var failedAttempts = 0;
        var notified = 0;
        await using (var lease = scope.Begin())
        {
            await WriteValue(99);
            scope.Current!.Enlist("memory", () => memory);
            scope.Current.RegisterAfterCommit("failing-notification", _ =>
            {
                failedAttempts++;
                throw new IOException("notification transport failed");
            });
            scope.Current.RegisterAfterCommit("next-notification", _ =>
            {
                Assert.Equal(99, SavedValue());
                Assert.True(memory.Applied);
                notified++;
                return ValueTask.CompletedTask;
            });
            await lease.CommitAsync(default);
        }

        Assert.Equal(99, SavedValue());
        Assert.Equal(1, failedAttempts);
        Assert.Equal(1, notified);
        Assert.True(memory.Applied);
        Assert.False(memory.RolledBack);
    }

    [Fact]
    public async Task Prepare_failure_rolls_back_saved_changes_and_never_sends_after_commit_notifications()
    {
        var cause = new IOException("participant prepare failed");
        var notified = 0;
        await using (var lease = scope.Begin())
        {
            await WriteValue(99);
            scope.Current!.Enlist("prepare-failure", () => new Participant { PrepareFailure = cause });
            scope.Current.RegisterAfterCommit("notification", _ => { notified++; return ValueTask.CompletedTask; });
            var failure = await Assert.ThrowsAsync<IOException>(async () => await lease.CommitAsync(default));
            Assert.Same(cause, failure);
        }

        Assert.Equal(10, SavedValue());
        Assert.Equal(0, notified);
    }

    [Fact]
    public async Task Fallible_commit_failure_never_sends_after_commit_notifications()
    {
        var cause = new IOException("storage commit failed");
        var storage = new Participant { CommitCanFail = true, CommitFailure = cause };
        var notified = 0;
        await using (var lease = scope.Begin())
        {
            scope.Current!.Enlist("storage", () => storage);
            scope.Current.RegisterAfterCommit("notification", _ => { notified++; return ValueTask.CompletedTask; });
            var failure = await Assert.ThrowsAsync<IOException>(async () => await lease.CommitAsync(default));
            Assert.Same(cause, failure);
        }

        Assert.False(storage.Applied);
        Assert.True(storage.RolledBack);
        Assert.Equal(0, notified);
    }

    private ValueTask WriteValue(int value) => database.WriteAsync(async (command, ct) =>
    {
        command.CommandText = "UPDATE after_commit_probe SET value=$value";
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(ct);
    }, default);

    private long SavedValue()
    {
        using var connection = new SqliteConnection(database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM after_commit_probe";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private sealed class Participant : ITransactionParticipant
    {
        public bool CommitCanFail { get; init; }
        public Exception? PrepareFailure { get; init; }
        public Exception? CommitFailure { get; init; }
        public bool Applied { get; private set; }
        public bool RolledBack { get; private set; }
        public ValueTask PrepareAsync(CancellationToken ct) => PrepareFailure is null ? ValueTask.CompletedTask : throw PrepareFailure;
        public ValueTask CommitAsync(CancellationToken ct)
        {
            if (CommitFailure is not null) throw CommitFailure;
            Applied = true;
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken ct)
        {
            Applied = false;
            RolledBack = true;
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        return ValueTask.CompletedTask;
    }
}
