using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Database;

public sealed class TransactionFailureSourceTests
{
    private static ItemSnapshot Pile(int count = 1) => ItemSnapshot.FromCore(StackableItemCore.Create(2, 3, count), true);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_SQLite_Flush_or_native_Commit_records_its_exact_source_phase_and_original_provider_cause(bool commit)
    {
        var path = Path.Combine(Path.GetTempPath(), "failure-source-" + Guid.NewGuid().ToString("N") + ".db");
        var cs = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ToString();
        await using var link = new SqliteConnection(cs); await link.OpenAsync();
        await using (var cmd = link.CreateCommand())
        { cmd.CommandText = commit ? "CREATE TABLE parent(id INTEGER PRIMARY KEY); CREATE TABLE probe(id INTEGER REFERENCES parent(id) DEFERRABLE INITIALLY DEFERRED);" : "CREATE TABLE probe(id INTEGER CHECK(id>0));"; await cmd.ExecuteNonQueryAsync(); }
        var participant = new SqliteDatabaseParticipant(cs);
        participant.Facet("actual statement", owner => new Statement(owner, "INSERT INTO probe VALUES(-1)"));
        Assert.Null(participant.LastFailure);
        SqliteException cause;
        if (commit) { await participant.PrepareAsync(default); cause = await Assert.ThrowsAsync<SqliteException>(() => participant.CommitAsync(default).AsTask()); }
        else cause = await Assert.ThrowsAsync<SqliteException>(() => participant.PrepareAsync(default).AsTask());
        var fact = participant.LastFailure!;
        Assert.Same(cause, fact.Cause); Assert.Same(participant, fact.Source); Assert.NotNull(fact.Occurrence);
        Assert.Equal(TransactionFailureKind.KnownStorage, fact.Kind);
        Assert.Equal(commit ? TransactionFailurePhase.FallibleCommit : TransactionFailurePhase.Prepare, fact.Phase);
        Assert.Equal(commit ? 787 : 275, cause.SqliteExtendedErrorCode);
        await participant.RollbackAsync(default);
        await using var count = link.CreateCommand(); count.CommandText = "SELECT count(*) FROM probe";
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Real_SQLite_facet_validation_preserves_the_custody_guard_and_rolls_back_created_rows()
    {
        var accessor = new Accessor();
        var path = Path.Combine(Path.GetTempPath(), "facet-source-" + Guid.NewGuid().ToString("N") + ".db");
        var db = new SqliteDatabase(path, accessor); var tx = new DomainTransaction(); accessor.Current = tx;
        var scope = ItemLifecycleScope.In(tx);
        var participant = tx.Enlist(db.ConnectionString, () => new SqliteDatabaseParticipant(db.ConnectionString));
        var session = SqliteItemDomainStoreSession.In(participant, scope);
        await session.CreateAsync(Pile(), default);
        var cause = await Assert.ThrowsAsync<InvalidOperationException>(() => participant.PrepareAsync(default).AsTask());
        Assert.Equal(TransactionFailureKind.Guard, participant.LastFailure!.Kind);
        Assert.Same(participant, participant.LastFailure.Source); Assert.Same(cause, participant.LastFailure.Cause);
        Assert.Equal(TransactionFailurePhase.Prepare, participant.LastFailure.Phase);
        Assert.Same(cause, scope.LastFailure!.Cause);
        await tx.AbortAsync(cause); accessor.Current = null;
        Assert.Equal(0, await db.ReadAsync(async (cmd, ct) => { cmd.CommandText = "SELECT count(*) FROM items"; return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)); }, default));
    }

    [Fact]
    public async Task Real_container_binding_version_guard_is_a_guard_fact_without_changing_the_original_cause()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var accessor = new Accessor(); var bindings = new CharacterContainerDomainService(accessor);
            var container = new MemoryItemContainer(new("binding-main"), new(InventoryContainerType.Main, 0), new RunItemDomainStore(), accessor, null!, null!);
            bindings.Register(new(1), container);
            var tx = new DomainTransaction(); accessor.Current = tx;
            Assert.Same(container, bindings.GetContainer(new(1), InventoryContainerType.Main));
            accessor.Current = null;
            Assert.True(bindings.UseContainer(new(1), container.Id)); // The actual owner increments its real version.
            accessor.Current = tx;
            var cause = await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync(default).AsTask());
            Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, tx.Completion);
            Assert.Equal(TransactionFailureKind.Guard, tx.CommitFailure!.Kind);
            Assert.Equal(TransactionFailurePhase.Prepare, tx.CommitFailure.Phase);
            Assert.Same(cause, tx.CommitFailure.Cause); Assert.Same(cause, tx.GuardFailure);
            accessor.Current = null; Assert.Same(container, bindings.GetContainer(new(1), InventoryContainerType.Main));
        });
    }

    [Fact]
    public async Task Real_volatile_store_version_guard_preserves_the_winning_store_state()
    {
        var store = new RunItemDomainStore(); var address = new ContainerId("volatile-real");
        var stale = new DomainTransaction(); var winner = new DomainTransaction();
        var old = store.SessionIn(stale); var next = store.SessionIn(winner);
        await old.PutAsync(address, 1, await old.CreateAsync(Pile(1), default), default);
        await next.PutAsync(address, 1, await next.CreateAsync(Pile(2), default), default);
        await winner.CommitAsync(default);
        var cause = await Assert.ThrowsAsync<InvalidOperationException>(() => stale.CommitAsync(default).AsTask());
        var fact = Assert.IsAssignableFrom<ITransactionFailureSource>(old).LastFailure!;
        Assert.Equal(TransactionFailureKind.Guard, fact.Kind); Assert.Equal(TransactionFailurePhase.Prepare, fact.Phase);
        Assert.Same(old, fact.Source); Assert.Same(cause, fact.Cause); Assert.Same(cause, stale.GuardFailure);
        var read = new DomainTransaction(); Assert.Equal(2, (await store.SessionIn(read).GetAsync(address, 1, default))!.Count);
        await read.RollbackAsync();
    }

    [Fact]
    public async Task Actual_lifecycle_and_unprepared_session_guards_remain_original_guard_causes()
    {
        var store = new RunItemDomainStore(); var tx = new DomainTransaction(); var session = store.SessionIn(tx);
        var participant = Assert.IsAssignableFrom<ITransactionParticipant>(session);
        Assert.False(participant.CommitCanFail);
        var unprepared = await Assert.ThrowsAsync<InvalidOperationException>(() => participant.CommitAsync(default).AsTask());
        var directGuard = Assert.IsAssignableFrom<ITransactionFailureSource>(session).LastFailure!;
        Assert.Same(unprepared, directGuard.Cause);
        Assert.Equal(TransactionFailureKind.Guard, directGuard.Kind);
        Assert.Equal(TransactionFailurePhase.Action, directGuard.Phase);
        await session.CreateAsync(Pile(), default);
        var scope = ItemLifecycleScope.In(tx);
        var custody = Assert.Throws<InvalidOperationException>(() => scope.ValidateForCommit());
        Assert.Same(custody, scope.LastFailure!.Cause); Assert.Equal(TransactionFailureKind.Guard, scope.LastFailure.Kind);
        await tx.RollbackAsync();
        var closed = Assert.Throws<InvalidOperationException>(() => scope.EnsureActive());
        Assert.Same(closed, scope.LastFailure.Cause); Assert.Equal(TransactionFailurePhase.Action, scope.LastFailure.Phase);
    }

    [Fact]
    public async Task SQLite_unprepared_commit_is_guard_and_unrecognized_Flush_error_is_not_known_storage()
    {
        var path = Path.Combine(Path.GetTempPath(), "unprepared-source-" + Guid.NewGuid().ToString("N") + ".db");
        var participant = new SqliteDatabaseParticipant(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        participant.Facet("not prepared", owner => new Statement(owner, ""));
        var guard = await Assert.ThrowsAsync<InvalidOperationException>(() => participant.CommitAsync(default).AsTask());
        Assert.Same(guard, participant.LastFailure!.Cause); Assert.Equal(TransactionFailureKind.Guard, participant.LastFailure.Kind);
        var unknown = new InvalidOperationException("controlled unrecognized facet failure");
        var second = new SqliteDatabaseParticipant(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        second.Facet("unknown", owner => new Statement(owner, "", unknown));
        Assert.Same(unknown, await Assert.ThrowsAsync<InvalidOperationException>(() => second.PrepareAsync(default).AsTask()));
        Assert.Null(second.LastFailure); await second.RollbackAsync(default);
    }

    private sealed class Accessor : IDomainTransactionAccessor { public IDomainTransaction? Current { get; set; } }
    private sealed class Statement(SqliteDatabaseParticipant database, string sql, Exception? failure = null) : ISqliteStoreFacet
    {
        public bool HasStagedWork => true;
        public async ValueTask FlushAsync(CancellationToken ct)
        {
            if (failure is not null) throw failure;
            await using var cmd = await database.CommandAsync(ct); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(ct);
        }
        public void Discard() { }
    }
}
