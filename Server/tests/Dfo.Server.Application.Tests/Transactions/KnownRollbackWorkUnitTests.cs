using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Dfo.Server.Application.Tests.Commands.ProtocolCommandExecutionTests;

namespace Dfo.Server.Application.Tests.Transactions;

public sealed class KnownRollbackWorkUnitTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Real_native_commit_rollback_returns_original_cause_after_cleanup_and_allows_a_later_unit()
    {
        var path = Path.Combine(Path.GetTempPath(), "opening-result-" + Guid.NewGuid().ToString("N") + ".db");
        await using var world = new CommandWorld(configure: services => services.AddSingleton(p => new SqliteDatabase(path, p.GetRequiredService<IDomainTransactionAccessor>())));
        var db = world.Services.GetRequiredService<SqliteDatabase>();
        await db.ReadAsync(async (cmd, ct) => { cmd.CommandText = "CREATE TABLE parent(id INTEGER PRIMARY KEY); CREATE TABLE child(id INTEGER REFERENCES parent(id) DEFERRABLE INITIALLY DEFERRED);"; return await cmd.ExecuteNonQueryAsync(ct); }, default);
        DomainTransaction? failed = null;
        world.Settings.Action = async context =>
        {
            var units = world.Services.GetRequiredService<ICommandWorkUnits>();
            var result = await units.WriteResultAsync(context, new("native", []), async (_, ct) =>
            {
                failed = (DomainTransaction)world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!;
                await db.WriteAsync(async (cmd, token) => { cmd.CommandText = "INSERT INTO child VALUES(404)"; await cmd.ExecuteNonQueryAsync(token); }, ct);
                return 7;
            });
            var rollback = Assert.IsType<CommandWriteResult<int>.CommitRolledBack>(result);
            var cause = Assert.IsType<SqliteException>(rollback.Cause);
            Assert.Equal(787, cause.SqliteExtendedErrorCode); Assert.Contains("SqliteTransaction.Commit", cause.StackTrace!);
            Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, failed!.Completion);
            Assert.Null(world.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
            return await units.WriteAsync(context, new("later", []), async (_, ct) =>
            { await db.WriteAsync(async (cmd, token) => { cmd.CommandText = "INSERT INTO parent VALUES(1)"; await cmd.ExecuteNonQueryAsync(token); }, ct); return 9; });
        };
        Assert.Equal(9, await world.Internal(new InternalProbe()));
        Assert.Equal(0, await Count(db, "child")); Assert.Equal(1, await Count(db, "parent"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_caught_business_cause_survives_an_independent_rollback_failure_even_when_the_same_object(bool sameObject)
    {
        await using var world = new CommandWorld();
        var business = new IOException("opening business cause"); var cleanup = sameObject ? business : new IOException("rollback cause");
        world.Settings.Action = async context =>
        {
            var result = await world.Services.GetRequiredService<ICommandWorkUnits>().WriteResultAsync(context, new("refused", []), (unit, _) =>
            {
                world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!.Enlist("rollback", () => new RecordingParticipant("rollback", [], false) { RollbackFailure = cleanup });
                unit.RequireRollback(business); return ValueTask.FromResult(7);
            });
            Assert.Fail("Rollback cleanup must not become a normal result: " + result.GetType().Name); return 0;
        };
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => world.Internal(new InternalProbe()).AsTask());
        var causes = Assert.IsType<AggregateException>(failure.InnerException).InnerExceptions;
        Assert.Equal(2, causes.Count); Assert.Same(business, causes[0]); Assert.Same(cleanup, causes[1]);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RequireRollback_cannot_turn_cancellation_or_uncertainty_into_a_normal_result(bool uncertain)
    {
        await using var world = new CommandWorld();
        Exception cause = uncertain ? new TransactionOutcomeUncertainException(TransactionUncertaintyReason.RollbackFailure, "uncertain", new IOException())
            : new OperationCanceledException("cancelled action");
        var returned = 0; var later = 0;
        world.Settings.Action = async context =>
        {
            try
            {
                await world.Services.GetRequiredService<ICommandWorkUnits>().WriteResultAsync(context, new("invalid refusal", []), (unit, _) =>
                { unit.RequireRollback(cause); return ValueTask.FromResult(7); });
                returned++;
            }
            catch (Exception) { }
            try { await world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync(context, new("after refusal", []), (_, _) => { later++; return ValueTask.FromResult(0); }); }
            catch (Exception) { }
            return 0;
        };
        var error = await Record.ExceptionAsync(() => world.Internal(new InternalProbe()).AsTask());
        Assert.NotNull(error); Assert.Equal(0, returned); Assert.Equal(0, later);
        Assert.Same(cause, world.Settings.Seen.Single().Request!.Failure);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_detached_SQLite_custody_guard_never_becomes_a_result_even_if_action_catches_it(bool caughtInAction)
    {
        var path = Path.Combine(Path.GetTempPath(), "opening-custody-" + Guid.NewGuid().ToString("N") + ".db");
        await using var world = new CommandWorld(configure: services => services.AddSingleton(p => new SqliteDatabase(path, p.GetRequiredService<IDomainTransactionAccessor>())));
        var db = world.Services.GetRequiredService<SqliteDatabase>();
        var store = new SqliteItemDomainStore(path); Exception? guard = null; var returned = 0;
        DomainTransaction? transaction = null;
        world.Settings.Action = async context =>
        {
            try
            {
                await world.Services.GetRequiredService<ICommandWorkUnits>().WriteResultAsync(context, new("custody", []), async (unit, ct) =>
                {
                    transaction = (DomainTransaction)world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!;
                    await store.SessionIn(transaction).CreateAsync(ItemSnapshot.FromCore(StackableItemCore.Create(2, 3, 1), true), ct);
                    if (caughtInAction)
                    {
                        try { ItemLifecycleScope.In(transaction).ValidateForCommit(); }
                        catch (InvalidOperationException cause) { guard = cause; unit.RequireRollback(cause); }
                    }
                    return 7;
                });
                returned++;
            }
            catch (InvalidOperationException cause) { guard ??= cause; }
            return 0;
        };
        var failure = await Record.ExceptionAsync(() => world.Internal(new InternalProbe()).AsTask());
        var rows = await Count(db, "items");
        output.WriteLine($"actual SQLite custody: caughtInAction={caughtInAction}; returned={returned}; rows={rows}; completion={transaction!.Completion}; cause={failure?.GetType().Name ?? "none"}");
        Assert.Equal(0, rows); Assert.IsType<InvalidOperationException>(failure);
        Assert.Same(guard, failure); Assert.Equal(0, returned);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, transaction.Completion);
        Assert.Same(guard, transaction.GuardFailure);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task Missing_stale_other_cause_phase_or_source_fact_is_unknown_and_sticky(int mismatch)
    {
        await using var world = new CommandWorld();
        Exception cause = mismatch == 6 ? new InvalidOperationException("unknown participant invariant") : new SqliteException("controlled participant, not provider storage", 19);
        var source = new FaultSource(cause, mismatch); DomainTransaction? transaction = null; var returned = 0; var later = 0;
        world.Settings.Action = async context =>
        {
            try
            {
                await world.Services.GetRequiredService<ICommandWorkUnits>().WriteResultAsync(context, new("unknown", []), (_, _) =>
                {
                    transaction = (DomainTransaction)world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!;
                    if (mismatch is 0 or 6) transaction.Enlist("no source", () => new RecordingParticipant("unknown", [], true) { PrepareFailure = cause });
                    else transaction.Enlist("source", () => source);
                    return ValueTask.FromResult(7);
                });
                returned++;
            }
            catch (Exception error) when (ReferenceEquals(error, cause)) { }
            try { await world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync(context, new("later", []), (_, _) => { later++; return ValueTask.FromResult(0); }); }
            catch (Exception error) when (ReferenceEquals(error, cause)) { }
            return 0;
        };
        Assert.Same(cause, await Record.ExceptionAsync(() => world.Internal(new InternalProbe()).AsTask()));
        Assert.Equal(0, returned); Assert.Equal(0, later); Assert.Equal(TransactionFailureKind.Unknown, transaction!.CommitFailure!.Kind);
    }
    private sealed class FaultSource : ITransactionParticipant, ITransactionFailureSource
    {
        private readonly Exception cause;
        private readonly int mismatch;
        public FaultSource(Exception cause, int mismatch)
        {
            this.cause = cause; this.mismatch = mismatch;
            if (mismatch == 1) LastFailure = new(this, cause, TransactionFailurePhase.Prepare, TransactionFailureKind.KnownStorage);
        }
        public bool CommitCanFail => true;
        public TransactionFailureFact? LastFailure { get; private set; }
        public ValueTask PrepareAsync(CancellationToken ct)
        {
            if (mismatch is not (1 or 5)) LastFailure = new(mismatch == 4 ? new object() : this,
                mismatch == 2 ? new SqliteException("other cause", 19) : cause,
                mismatch == 3 ? TransactionFailurePhase.FallibleCommit : TransactionFailurePhase.Prepare, TransactionFailureKind.KnownStorage);
            throw cause;
        }
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Refused_action_and_a_later_independent_current_failure_keep_both_original_causes()
    {
        await using var world = new CommandWorld();
        var business = new IOException("business refused"); var current = new IOException("request independently failed");
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteResultAsync(context, new("refusal then current", []), (unit, _) =>
            { unit.RequireRollback(business); context.Request!.Failed(current); return ValueTask.FromResult(7); });
            return 0;
        };
        var failure = await Assert.ThrowsAsync<AggregateException>(() => world.Internal(new InternalProbe()).AsTask());
        Assert.Equal(new Exception[] { business, current }, failure.InnerExceptions);
    }

    [Fact]
    public async Task A_real_SQLite_Prepare_Flush_failure_returns_its_cause_and_rolls_back_prior_actual_asset_writes()
    {
        var path = Path.Combine(Path.GetTempPath(), "prepare-result-" + Guid.NewGuid().ToString("N") + ".db");
        await using var world = new CommandWorld(configure: services => services.AddSingleton(p => new SqliteDatabase(path, p.GetRequiredService<IDomainTransactionAccessor>())));
        var db = world.Services.GetRequiredService<SqliteDatabase>();
        await db.ReadAsync(async (cmd, ct) => { cmd.CommandText = "CREATE TABLE prior_asset(id INTEGER); CREATE TABLE flush_target(id INTEGER CHECK(id>0));"; return await cmd.ExecuteNonQueryAsync(ct); }, default);
        world.Settings.Action = async context =>
        {
            var result = await world.Services.GetRequiredService<ICommandWorkUnits>().WriteResultAsync(context, new("real prepare", []), async (_, ct) =>
            {
                var transaction = world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!;
                transaction.Enlist(db.ConnectionString, () => new SqliteDatabaseParticipant(db.ConnectionString)).Facet("real Flush", owner => new FlushFailure(owner));
                await db.WriteAsync(async (cmd, token) => { cmd.CommandText = "INSERT INTO prior_asset VALUES(1)"; await cmd.ExecuteNonQueryAsync(token); }, ct);
                return 7;
            });
            var failed = Assert.IsType<CommandWriteResult<int>.CommitRolledBack>(result);
            Assert.Equal(275, Assert.IsType<SqliteException>(failed.Cause).SqliteExtendedErrorCode);
            Assert.Contains("FlushFailure.FlushAsync", failed.Cause.StackTrace!); Assert.Null(context.Request!.Failure); return 1;
        };
        Assert.Equal(1, await world.Internal(new InternalProbe())); Assert.Equal(0, await Count(db, "prior_asset")); Assert.Equal(0, await Count(db, "flush_target"));
    }
    private sealed class FlushFailure(SqliteDatabaseParticipant database) : ISqliteStoreFacet
    {
        public bool HasStagedWork => true;
        public async ValueTask FlushAsync(CancellationToken ct)
        { await using var cmd = await database.CommandAsync(ct); cmd.CommandText = "INSERT INTO flush_target VALUES(0)"; await cmd.ExecuteNonQueryAsync(ct); }
        public void Discard() { }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task An_unawaited_result_is_drained_and_unfinished_guard_prevents_native_commit_while_preserving_cleanup(bool failRollback)
    {
        var path = Path.Combine(Path.GetTempPath(), "drained-result-" + Guid.NewGuid().ToString("N") + ".db");
        await using var world = new CommandWorld(configure: services => services.AddSingleton(p => new SqliteDatabase(path, p.GetRequiredService<IDomainTransactionAccessor>())));
        var db = world.Services.GetRequiredService<SqliteDatabase>();
        await db.ReadAsync(async (cmd, ct) => { cmd.CommandText = "CREATE TABLE parent(id INTEGER PRIMARY KEY); CREATE TABLE child(id INTEGER REFERENCES parent(id) DEFERRABLE INITIALLY DEFERRED);"; return await cmd.ExecuteNonQueryAsync(ct); }, default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var assigned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new IOException("controlled independent rollback"); Task<CommandWriteResult<int>>? pending = null;
        CommandRequestContext? observed = null;
        world.Settings.Action = context =>
        {
            observed = context.Request;
            pending = world.Services.GetRequiredService<ICommandWorkUnits>().WriteResultAsync(context, new("drained native", []), async (_, ct) =>
            {
                if (failRollback) world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!.Enlist("cleanup", () => new RecordingParticipant("cleanup", [], false) { RollbackFailure = cleanup });
                await db.WriteAsync(async (cmd, token) => { cmd.CommandText = "INSERT INTO child VALUES(404)"; await cmd.ExecuteNonQueryAsync(token); }, ct);
                entered.TrySetResult(); await release.Task; return 7;
            }).AsTask();
            assigned.TrySetResult();
            return ValueTask.FromResult(0);
        };
        var job = world.Internal(new InternalProbe()).AsTask();
        try
        {
            await assigned.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(job.IsCompleted); Assert.False(pending!.IsCompleted);
            // Assignment/entry does not mean the invoker has returned. Observe the real bus
            // guard before releasing the action, so this test cannot race a native commit.
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            while (observed!.Failure is null)
            {
                if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(10))
                    throw new TimeoutException("The real unfinished-work-unit guard did not arrive.");
                await Task.Yield();
            }
            var guard = Assert.IsType<InvalidOperationException>(observed.Failure);
            Assert.Contains("unfinished work unit", guard.Message);
            Assert.False(job.IsCompleted); Assert.False(pending.IsCompleted);
            output.WriteLine("Observed the real unfinished-work-unit guard before releasing the action.");
        }
        finally { release.TrySetResult(); }
        if (failRollback)
        {
            var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => job);
            var causes = Assert.IsType<AggregateException>(failure.InnerException).InnerExceptions;
            Assert.Contains(cleanup, causes); Assert.Contains(causes, error => error is InvalidOperationException);
            Assert.DoesNotContain(causes, error => error is SqliteException);
        }
        else
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => job);
            Assert.Contains("unfinished work unit", failure.Message);
        }
        Assert.True(pending!.IsCompleted);
        Assert.Equal(0, await Count(db, "child"));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Action_Prepare_and_fallible_Commit_cancellation_never_return_a_value_or_allow_a_later_write(int stage)
    {
        await using var world = new CommandWorld(); var cause = new OperationCanceledException("controlled cancellation phase");
        var returned = 0; var later = 0;
        world.Settings.Action = async context =>
        {
            try
            {
                await world.Services.GetRequiredService<ICommandWorkUnits>().WriteResultAsync<int>(context, new("cancel phase", []), (_, _) =>
                {
                    if (stage == 0) throw cause;
                    world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!.Enlist("cancel", () => new RecordingParticipant("cancel", [], true)
                    { PrepareFailure = stage == 1 ? cause : null, CommitFailure = stage == 2 ? cause : null });
                    return ValueTask.FromResult(7);
                }); returned++;
            }
            catch (OperationCanceledException) { }
            try { await world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync(context, new("later", []), (_, _) => { later++; return ValueTask.FromResult(0); }); }
            catch (OperationCanceledException) { }
            return 0;
        };
        Assert.IsAssignableFrom<OperationCanceledException>(await Record.ExceptionAsync(() => world.Internal(new InternalProbe()).AsTask()));
        Assert.Equal(0, returned); Assert.Equal(0, later); Assert.Same(cause, world.Settings.Seen.Single().Request!.Failure);
    }

    [Fact]
    public async Task New_result_cannot_recover_a_nonfallible_violation_after_real_SQLite_commit()
    {
        var path = Path.Combine(Path.GetTempPath(), "durable-result-" + Guid.NewGuid().ToString("N") + ".db");
        await using var world = new CommandWorld(configure: services => services.AddSingleton(p => new SqliteDatabase(path, p.GetRequiredService<IDomainTransactionAccessor>())));
        var db = world.Services.GetRequiredService<SqliteDatabase>();
        await db.ReadAsync(async (cmd, ct) => { cmd.CommandText = "CREATE TABLE durable_asset(id INTEGER)"; return await cmd.ExecuteNonQueryAsync(ct); }, default);
        var violation = new IOException("controlled nonfallible violation"); var returned = 0;
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteResultAsync(context, new("durable then violation", []), async (_, ct) =>
            {
                await db.WriteAsync(async (cmd, token) => { cmd.CommandText = "INSERT INTO durable_asset VALUES(1)"; await cmd.ExecuteNonQueryAsync(token); }, ct);
                world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!.Enlist("nonfallible", () => new RecordingParticipant("nonfallible", [], false) { CommitFailure = violation });
                return 7;
            }); returned++; return 0;
        };
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => world.Internal(new InternalProbe()).AsTask());
        Assert.Equal(TransactionUncertaintyReason.NonFallibleCommitContractViolation, failure.Reason); Assert.Same(violation, failure.InnerException);
        Assert.Equal(0, returned); Assert.Equal(1, await Count(db, "durable_asset"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_second_rejected_rollback_declaration_keeps_the_first_business_cause_and_independent_typed_or_cancelled_cause(bool uncertain)
    {
        await using var world = new CommandWorld();
        var business = new IOException("first ordinary refusal");
        Exception second = uncertain ? new TransactionOutcomeUncertainException(TransactionUncertaintyReason.RollbackFailure, "independent second declaration", new IOException())
            : new OperationCanceledException("independent second declaration");
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteResultAsync(context, new("two declarations", []), (unit, _) =>
            { unit.RequireRollback(business); unit.RequireRollback(second); return ValueTask.FromResult(7); });
            return 0;
        };
        var failure = await Assert.ThrowsAsync<AggregateException>(() => world.Internal(new InternalProbe()).AsTask());
        Assert.Equal(2, failure.InnerExceptions.Count); Assert.Same(business, failure.InnerExceptions[0]); Assert.Same(second, failure.InnerExceptions[1]);
    }

    private static ValueTask<int> Count(SqliteDatabase db, string table) => db.ReadAsync(async (cmd, ct) =>
    { cmd.CommandText = "SELECT count(*) FROM " + table; return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)); }, default);
}
