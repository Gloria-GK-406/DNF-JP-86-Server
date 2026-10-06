using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Database;

/// <summary>
/// The one database, reached the way the use case currently running reaches it.
/// </summary>
/// <remarks>
/// Every repository asks here for its statements instead of opening a connection. That is the
/// whole point: a repository that opens its own connection is its own transaction, so two of them
/// in one use case can never settle together and two reads can never describe one moment.
///
/// Which connection a statement lands on is not the repository's business and not decided by what
/// it was handed at construction. It follows the use case currently on this flow, which is why
/// nothing here is passed down through a signature.
/// </remarks>
internal sealed class SqliteDatabase
{
    private readonly IDomainTransactionAccessor transactions;

    /// <remarks>
    /// Putting the database in shape happens here, once, rather than in each repository's
    /// constructor. A repository is about one table; which tables exist is about the file, and the
    /// file is what this is. It also removes the old arrangement's surprise, where whether a table
    /// existed depended on which repository had been asked for first.
    /// </remarks>
    public SqliteDatabase(string databasePath, IDomainTransactionAccessor transactions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(transactions);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        this.ConnectionString =
            new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        this.transactions = transactions;
        CharacterDatabaseSchema.Ensure(this.ConnectionString);
    }

    /// <summary>What enlistment is keyed on, shared with every store over the same file.</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Reads through the running use case, or on a connection of its own when there is none.
    /// </summary>
    /// <remarks>
    /// A read outside a use case is allowed because reading is not a change: it has nothing to
    /// settle and belongs to no outcome. Refusing it would push a boundary onto every caller that
    /// only wanted to look at something, and there are callers -- a login checking an account
    /// exists -- that legitimately have none.
    /// </remarks>
    public async ValueTask<TResult> ReadAsync<TResult>(
        Func<SqliteCommand, CancellationToken, ValueTask<TResult>> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (this.Participant() is { } ambient)
        {
            await using var joined = await ambient.CommandAsync(cancellationToken)
                .ConfigureAwait(false);
            return await read(joined, cancellationToken).ConfigureAwait(false);
        }

        await using var connection = new SqliteConnection(this.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var alone = connection.CreateCommand();
        return await read(alone, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes through the running use case.</summary>
    /// <remarks>
    /// A write outside a use case is refused rather than carried out on a connection of its own.
    /// Doing it alone would look like it worked and would not be taken back by whatever the caller
    /// was in the middle of -- which is the failure this whole arrangement exists to remove, and
    /// the one that no test would show, because a lone write does succeed.
    /// </remarks>
    public async ValueTask WriteAsync(
        Func<SqliteCommand, CancellationToken, ValueTask> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (this.transactions.Current?.IsReadOnly == true)
            throw new InvalidOperationException("A read-only query cannot write to the database.");
        var ambient = this.Participant()
            ?? throw new InvalidOperationException(
                "This database keeps what is written to it past this process, so it is only "
                + "changed inside a use case.");
        await using var command = await ambient.CommandAsync(cancellationToken)
            .ConfigureAwait(false);
        await write(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>This database's part in the use case running on this flow, if there is one.</summary>
    private SqliteDatabaseParticipant? Participant() =>
        this.transactions.Current is { } ambient
            ? ambient.Enlist(
                this.ConnectionString, () => new SqliteDatabaseParticipant(this.ConnectionString, ambient.IsReadOnly))
            : null;
}
