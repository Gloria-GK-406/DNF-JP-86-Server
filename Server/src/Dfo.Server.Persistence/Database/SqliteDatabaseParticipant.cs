using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Transactions;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Database;

/// <summary>
/// Everything one use case writes to one database, settled as a single participant.
/// </summary>
/// <remarks>
/// The participant is the database, not the store. That is forced rather than chosen: a
/// transaction admits at most one participant whose commit can still fail, so if every store on a
/// database joined on its own, the second one would be refused and no use case could ever change
/// two kinds of state at once. Sharing one connection and one database transaction also makes the
/// guarantee real rather than approximate -- two connections would settle independently however
/// carefully they were ordered.
///
/// Each kind of state contributes a <see cref="ISqliteStoreFacet"/>. Facets stage their own writes
/// in their own vocabulary and are asked to flush during prepare; they never open a connection,
/// begin a transaction, or commit.
/// </remarks>
internal sealed class SqliteDatabaseParticipant(string connectionString) : ITransactionParticipant
{
    private readonly Dictionary<object, ISqliteStoreFacet> facets = [];

    /// <summary>The facets in the order they first joined, which is the order they are flushed.</summary>
    /// <remarks>
    /// Kept beside the lookup rather than relying on the dictionary's enumeration order, which is
    /// not part of its contract. Two facets writing the same rows would otherwise depend on an
    /// order nothing states and nothing preserves.
    /// </remarks>
    private readonly List<ISqliteStoreFacet> order = [];

    private SqliteConnection? connection;

    private SqliteTransaction? pending;

    private bool prepared;

    public bool CommitCanFail => true;

    /// <summary>The facet registered under <paramref name="key"/>, creating one on first use.</summary>
    /// <remarks>
    /// Keyed like enlistment itself, so one kind of state asked for repeatedly during one use case
    /// contributes exactly once and its reads see its own staged writes.
    /// </remarks>
    public TFacet Facet<TFacet>(object key, Func<SqliteDatabaseParticipant, TFacet> create)
        where TFacet : class, ISqliteStoreFacet
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(create);
        if (this.facets.TryGetValue(key, out var existing))
        {
            return existing as TFacet
                ?? throw new InvalidOperationException(
                    $"'{key}' already contributes as '{existing.GetType().Name}', which is not a "
                    + $"'{typeof(TFacet).Name}'.");
        }

        var facet = create(this);
        this.facets.Add(key, facet);
        this.order.Add(facet);
        return facet;
    }

    /// <summary>
    /// A command on this use case's connection, inside its database transaction, opening the
    /// connection and beginning the transaction on first use.
    /// </summary>
    /// <remarks>
    /// Facets are given commands rather than the connection, so that a statement cannot be issued
    /// outside the transaction this participant is holding open. Handing out the connection would
    /// leave every facet responsible for remembering to attach it.
    ///
    /// The transaction begins here rather than at prepare because reads need it too. A use case
    /// that reads several tables is asking one question in several statements; without a
    /// transaction spanning them, someone else's commit can land in the middle and the answer
    /// describes two different worlds with nothing to say which parts came from which.
    ///
    /// It is deferred on purpose. The default takes the database's one write lock at once, which
    /// would make every use case -- including a character entering the game, which only reads --
    /// stop everyone else from writing for as long as it ran. Deferred takes only what each
    /// statement needs, so a use case that never writes never blocks a use case that does.
    /// </remarks>
    internal async ValueTask<SqliteCommand> CommandAsync(CancellationToken cancellationToken)
    {
        var link = await this.OpenAsync(cancellationToken).ConfigureAwait(false);
        this.pending ??= link.BeginTransaction(deferred: true);
        var command = link.CreateCommand();
        command.Transaction = this.pending;
        return command;
    }

    private async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (this.connection is not null)
        {
            return this.connection;
        }

        var link = new SqliteConnection(connectionString);
        try
        {
            await link.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Nothing else holds it yet, so nothing else would ever close it.
            await link.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        this.connection = link;
        return link;
    }

    /// <remarks>
    /// Every facet's staged writes are applied here, inside a database transaction that is left
    /// open. What remains for the commit is the one act that can still fail on its own.
    ///
    /// The marker is set last: a prepare that threw partway must leave a commit unable to stand in
    /// for it, and the coordinator's rollback is what undoes the statements already applied.
    /// </remarks>
    public async ValueTask PrepareAsync(CancellationToken cancellationToken)
    {
        // Custody validation must also run after immediate writes or a mid-use-case flush.
        foreach (var facet in this.order) facet.ValidateForCommit();
        if (!this.order.Any(facet => facet.HasStagedWork))
        {
            this.prepared = true;
            return;
        }

        var link = await this.OpenAsync(cancellationToken).ConfigureAwait(false);
        // Whoever read first already began it, and beginning a second one on the same connection
        // is not a thing SQLite offers. Deferred like the reads' one: the write lock is taken by
        // the first statement below that needs it, not by the act of starting.
        this.pending ??= link.BeginTransaction(deferred: true);

        foreach (var facet in this.order)
        {
            await facet.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        this.prepared = true;
    }

    /// <remarks>
    /// A commit reached without a prepare is refused rather than quietly carrying out the writes
    /// itself. Doing them here would make this the second act of the use case that can fail, which
    /// is what the one-fallible-participant rule exists to prevent, and dropping them silently
    /// would surface as lost state rather than as an exception.
    /// </remarks>
    public ValueTask CommitAsync(CancellationToken cancellationToken) =>
        this.CompleteAsync(commit: true, cancellationToken);

    public ValueTask RollbackAsync(CancellationToken cancellationToken) =>
        this.CompleteAsync(commit: false, cancellationToken);

    private async ValueTask CompleteAsync(bool commit, CancellationToken cancellationToken)
    {
        Exception? operationFailure = null;
        var durableCommitted = false;
        try
        {
            if (commit && !this.prepared && this.order.Any(facet => facet.HasStagedWork))
                throw new InvalidOperationException(
                    "The database was committed before it was prepared, so it wrote nothing.");
            if (this.pending is not null)
            {
                if (commit)
                {
                    await this.pending.CommitAsync(cancellationToken).ConfigureAwait(false);
                    durableCommitted = true;
                }
                else
                    await this.pending.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception failure)
        {
            operationFailure = failure;
        }

        try
        {
            await this.SettleAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupFailure)
        {
            throw new TransactionOutcomeUncertainException(
                durableCommitted
                    ? TransactionUncertaintyReason.PostDurableCleanupFailure
                    : TransactionUncertaintyReason.SettlementCleanupFailure,
                "The database settlement could not release all of its resources reliably.",
                operationFailure is null ? cleanupFailure : new AggregateException(operationFailure, cleanupFailure));
        }
        if (operationFailure is not null)
            ExceptionDispatchInfo.Capture(operationFailure).Throw();
    }

    // Try every cleanup even if one fails, and preserve every cause. In particular, a facet's
    // Discard must never hide the earlier commit/rollback or transaction disposal failure.
    private async ValueTask SettleAsync()
    {
        List<Exception> failures = [];
        var transaction = this.pending;
        this.pending = null;
        this.prepared = false;
        if (transaction is not null)
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) { failures.Add(failure); }
        }
        foreach (var facet in this.order)
        {
            try { facet.Discard(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        var link = this.connection;
        this.connection = null;
        if (link is not null)
        {
            try { await link.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0)
            throw new AggregateException("Database settlement cleanup failed.", failures);
    }

}

/// <summary>One kind of state's contribution to what a database writes in one use case.</summary>
/// <remarks>
/// A facet holds its own staged writes in its own vocabulary. It is told when to turn them into
/// statements, and when to let them go; it decides nothing about the outcome. It reaches the
/// database only through the commands its participant hands it, so it cannot issue a statement
/// outside the transaction being held open.
/// </remarks>
internal interface ISqliteStoreFacet
{
    /// <summary>Whether this facet has anything to write.</summary>
    bool HasStagedWork { get; }

    /// <summary>Final invariants, separate from materializing writes during a use case.</summary>
    void ValidateForCommit() { }

    /// <summary>Turns the staged writes into statements, inside the open transaction.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken);

    /// <summary>Lets go of the staged writes, whatever became of them.</summary>
    void Discard();
}
