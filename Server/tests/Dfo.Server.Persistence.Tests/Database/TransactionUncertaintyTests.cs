using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Database;

public sealed class TransactionUncertaintyTests
{
    [Fact]
    public async Task EveryFacetIsDiscardedAndAllCleanupCausesSurviveAfterDurableCommit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = new IOException("first facet");
        var second = new IOException("second facet");
        fixture.Participant.Facet("first", owner => new Facet(owner, first));
        fixture.Participant.Facet("second", owner => new Facet(owner, second));
        await fixture.Participant.PrepareAsync(default);
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () =>
            await fixture.Participant.CommitAsync(default));
        Assert.Contains(first, Causes(failure));
        Assert.Contains(second, Causes(failure));
        Assert.Equal(2, await fixture.CountAsync());
    }

    [Fact]
    public async Task RealDurableCommitThenNonFallibleViolationIsUncertainAndRemainsCommitted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new DomainTransaction();
        var database = transaction.Enlist("db", () => fixture.Participant);
        database.Facet("data", owner => new Facet(owner));
        var cause = new IOException("memory commit failed");
        transaction.Enlist("memory", () => new FailingParticipant(commitFailure: cause));
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () =>
            await transaction.CommitAsync(default));
        Assert.Equal(TransactionUncertaintyReason.NonFallibleCommitContractViolation, failure.Reason);
        Assert.Same(cause, failure.InnerException);
        Assert.Equal(1, await fixture.CountAsync());
    }

    [Fact]
    public async Task RealFacetCleanupFailureAfterDurableCommitIsTyped()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cleanupCause = new IOException("facet discard failed");
        fixture.Participant.Facet("data", owner => new Facet(owner, cleanupCause));
        await fixture.Participant.PrepareAsync(default);
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () =>
            await fixture.Participant.CommitAsync(default));
        Assert.Equal(TransactionUncertaintyReason.PostDurableCleanupFailure, failure.Reason);
        Assert.Contains(cleanupCause, Causes(failure));
        Assert.Equal(1, await fixture.CountAsync());
    }

    [Fact]
    public async Task ConfirmedPrepareRollbackPreservesOriginalIOException()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new DomainTransaction();
        transaction.Enlist("db", () => fixture.Participant).Facet("data", owner => new Facet(owner));
        var cause = new IOException("late prepare failed");
        transaction.Enlist("late", () => new FailingParticipant(prepareFailure: cause));
        var failure = await Assert.ThrowsAsync<IOException>(async () => await transaction.CommitAsync(default));
        Assert.Same(cause, failure);
        Assert.False(TransactionOutcomeUncertainException.Contains(failure));
        Assert.Equal(0, await fixture.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealDeferredConstraintCommitFailurePreservesItsCauseAndCleanupCause(bool failCleanup)
    {
        await using var fixture = await Fixture.CreateAsync(deferredConstraint: true);
        var cleanupCause = failCleanup ? new IOException("discard failed after commit failure") : null;
        fixture.Participant.Facet("data", owner => new Facet(owner, cleanupCause));
        await fixture.Participant.PrepareAsync(default); // INSERT succeeds; deferred FK fails at COMMIT.
        if (failCleanup)
        {
            var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () =>
                await fixture.Participant.CommitAsync(default));
            Assert.Equal(TransactionUncertaintyReason.SettlementCleanupFailure, failure.Reason);
            Assert.Contains(Causes(failure), error => error is SqliteException { SqliteErrorCode: 19 });
            Assert.Contains(cleanupCause!, Causes(failure));
        }
        else
        {
            var failure = await Assert.ThrowsAsync<SqliteException>(async () => await fixture.Participant.CommitAsync(default));
            Assert.Equal(19, failure.SqliteErrorCode);
            Assert.False(TransactionOutcomeUncertainException.Contains(failure));
        }
        Assert.Equal(0, await fixture.CountAsync());
    }

    [Fact]
    public async Task RealInvalidatedRollbackAndCleanupFailureAreBothPreserved()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cleanupCause = new IOException("discard failed after rollback failure");
        fixture.Participant.Facet("data", owner => new Facet(owner, cleanupCause));
        await fixture.Participant.PrepareAsync(default);
        await using (var command = await fixture.Participant.CommandAsync(default))
            command.Connection!.Close(); // Actual SQLite transaction is now invalid, not a stand-in commit fault.
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () =>
            await fixture.Participant.RollbackAsync(default));
        Assert.Contains(Causes(failure), error => error is InvalidOperationException);
        Assert.Contains(cleanupCause, Causes(failure));
        Assert.Equal(0, await fixture.CountAsync());
    }

    private static IEnumerable<Exception> Causes(Exception error)
    {
        yield return error;
        if (error is AggregateException aggregate)
        {
            foreach (var cause in aggregate.InnerExceptions)
                foreach (var child in Causes(cause)) yield return child;
        }
        else if (error.InnerException is { } inner)
            foreach (var child in Causes(inner)) yield return child;
    }

    private sealed class Facet(SqliteDatabaseParticipant database, Exception? cleanupFailure = null) : ISqliteStoreFacet
    {
        public bool HasStagedWork => true;
        public async ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            await using var command = await database.CommandAsync(cancellationToken);
            command.CommandText = "INSERT INTO changes(id) VALUES(1);";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        public void Discard() { if (cleanupFailure is not null) throw cleanupFailure; }
    }

    private sealed class FailingParticipant(Exception? prepareFailure = null, Exception? commitFailure = null)
        : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => prepareFailure is null ? ValueTask.CompletedTask : throw prepareFailure;
        public ValueTask CommitAsync(CancellationToken ct) => commitFailure is null ? ValueTask.CompletedTask : throw commitFailure;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class Fixture(string path) : IAsyncDisposable
    {
        private readonly string connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString();
        public SqliteDatabaseParticipant Participant { get; } = new(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString());
        public static async Task<Fixture> CreateAsync(bool deferredConstraint = false)
        {
            var fixture = new Fixture(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"effects-transaction-{Guid.NewGuid():N}.db"));
            await using var connection = new SqliteConnection(fixture.connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = deferredConstraint
                ? "CREATE TABLE parents(id INTEGER PRIMARY KEY); CREATE TABLE changes(id INTEGER REFERENCES parents(id) DEFERRABLE INITIALLY DEFERRED);"
                : "CREATE TABLE changes(id INTEGER);";
            await command.ExecuteNonQueryAsync();
            return fixture;
        }
        public async Task<long> CountAsync()
        {
            await using var connection = new SqliteConnection(this.connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM changes;";
            return (long)(await command.ExecuteScalarAsync())!;
        }
        public async ValueTask DisposeAsync()
        {
            try { await this.Participant.RollbackAsync(default); }
            catch (TransactionOutcomeUncertainException) { }
            File.Delete(path);
        }
    }
}
