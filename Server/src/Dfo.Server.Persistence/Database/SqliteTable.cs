using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Database;

/// <summary>
/// The four things a repository over one table ever does.
/// </summary>
/// <remarks>
/// Every table repository here is the same shape: name the statement, bind the values it takes,
/// say how a row becomes a value. What differs between them is the statement, not the plumbing --
/// so the plumbing is written once and the statements stay where they belong, beside the table
/// they are about.
///
/// The statements are written out rather than derived from the type. This project's tables are the
/// original server's, not this code's: columns are named as that server named them, several hold
/// packed bytes rather than fields, and a few writes carry a condition that decides whether they
/// happen at all. A mapper that inferred any of it would have to be told all three exceptions,
/// which is more to explain than the statements are.
/// </remarks>
internal static class SqliteTable
{
    /// <summary>Every row the statement returns, in the order it returns them.</summary>
    public static ValueTask<IReadOnlyList<TRow>> ReadAllAsync<TRow>(
        this SqliteDatabase database,
        string statement,
        Action<SqliteParameterCollection> bind,
        Func<SqliteDataReader, TRow> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        return database.ReadAsync(
            async (command, token) =>
            {
                Prepare(command, statement, bind);
                var rows = new List<TRow>();
                await using var reader = await command.ExecuteReaderAsync(token)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    rows.Add(read(reader));
                }

                return (IReadOnlyList<TRow>)rows;
            },
            cancellationToken);
    }

    /// <summary>The first row the statement returns, or nothing when it returns none.</summary>
    public static ValueTask<TRow?> ReadFirstAsync<TRow>(
        this SqliteDatabase database,
        string statement,
        Action<SqliteParameterCollection> bind,
        Func<SqliteDataReader, TRow> read,
        CancellationToken cancellationToken)
        where TRow : class
    {
        ArgumentNullException.ThrowIfNull(database);
        return database.ReadAsync(
            async (command, token) =>
            {
                Prepare(command, statement, bind);
                await using var reader = await command.ExecuteReaderAsync(token)
                    .ConfigureAwait(false);
                return await reader.ReadAsync(token).ConfigureAwait(false) ? read(reader) : null;
            },
            cancellationToken);
    }

    /// <summary>
    /// Carries the statement out and reads back the one row it answers with.
    /// </summary>
    /// <remarks>
    /// A statement that changes the database is a write however it is phrased, so this goes
    /// through the write path and is refused outside a use case like any other. Reading it back
    /// through the read path would have been the one way to change a database with nothing to
    /// settle the change.
    /// </remarks>
    public static ValueTask<TRow?> WriteReturningAsync<TRow>(
        this SqliteDatabase database,
        string statement,
        Action<SqliteParameterCollection> bind,
        Func<SqliteDataReader, TRow> read,
        CancellationToken cancellationToken)
        where TRow : class
    {
        ArgumentNullException.ThrowIfNull(database);
        TRow? answer = null;
        return WriteThenAnswer();

        async ValueTask<TRow?> WriteThenAnswer()
        {
            await database.WriteAsync(
                async (command, token) =>
                {
                    Prepare(command, statement, bind);
                    await using var reader = await command.ExecuteReaderAsync(token)
                        .ConfigureAwait(false);
                    answer = await reader.ReadAsync(token).ConfigureAwait(false)
                        ? read(reader)
                        : null;
                },
                cancellationToken).ConfigureAwait(false);
            return answer;
        }
    }

    /// <summary>Carries the statement out, and says how many rows it touched.</summary>
    public static ValueTask<int> ExecuteAsync(
        this SqliteDatabase database,
        string statement,
        Action<SqliteParameterCollection> bind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        var touched = 0;
        return WriteThenCount();

        async ValueTask<int> WriteThenCount()
        {
            await database.WriteAsync(
                async (command, token) =>
                {
                    Prepare(command, statement, bind);
                    touched = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            return touched;
        }
    }

    private static void Prepare(
        SqliteCommand command, string statement, Action<SqliteParameterCollection> bind)
    {
        command.CommandText = statement;
        bind(command.Parameters);
    }
}
