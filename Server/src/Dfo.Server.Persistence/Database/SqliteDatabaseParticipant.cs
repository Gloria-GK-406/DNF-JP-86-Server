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
    /// connection on first use.
    /// </summary>
    /// <remarks>
    /// Facets are given commands rather than the connection, so that a statement cannot be issued
    /// outside the transaction this participant is holding open. Handing out the connection would
    /// leave every facet responsible for remembering to attach it.
    /// </remarks>
    internal async ValueTask<SqliteCommand> CommandAsync(CancellationToken cancellationToken)
    {
        var link = await this.OpenAsync(cancellationToken).ConfigureAwait(false);
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
        if (!this.order.Any(facet => facet.HasStagedWork))
        {
            this.prepared = true;
            return;
        }

        var link = await this.OpenAsync(cancellationToken).ConfigureAwait(false);
        this.pending = (SqliteTransaction)await link
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

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
    public async ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        if (!this.prepared && this.order.Any(facet => facet.HasStagedWork))
        {
            await this.SettleAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                "The database was committed before it was prepared, so it wrote nothing.");
        }

        try
        {
            if (this.pending is not null)
            {
                await this.pending.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await this.SettleAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask RollbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (this.pending is not null)
            {
                await this.pending.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await this.SettleAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Lets go of everything this use case held, however it ended.</summary>
    /// <remarks>
    /// Disposing an uncompleted transaction rolls it back, which can itself fail on a connection
    /// that has already broken. The connection is closed regardless, because a use case that
    /// cannot tidy up is exactly the one whose handle must not be left held.
    /// </remarks>
    private async ValueTask SettleAsync()
    {
        try
        {
            if (this.pending is not null)
            {
                await this.pending.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            this.pending = null;
            this.prepared = false;
            try
            {
                foreach (var facet in this.order)
                {
                    facet.Discard();
                }
            }
            finally
            {
                await this.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask CloseAsync()
    {
        if (this.connection is null)
        {
            return;
        }

        await this.connection.DisposeAsync().ConfigureAwait(false);
        this.connection = null;
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

    /// <summary>Turns the staged writes into statements, inside the open transaction.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken);

    /// <summary>Lets go of the staged writes, whatever became of them.</summary>
    void Discard();
}
