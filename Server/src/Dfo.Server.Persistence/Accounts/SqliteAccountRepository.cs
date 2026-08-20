using System.Globalization;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Accounts;

internal sealed class SqliteAccountRepository : IAccountRepository
{
    private readonly string connectionString;

    public SqliteAccountRepository(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path cannot be empty.", nameof(databasePath));
        }

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        EnsureSchema();
    }

    public async ValueTask<AccountRecord> GetOrCreateAsync(
        string loginId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(loginId) || loginId.Length > 256)
        {
            throw new ArgumentException("Login ID must contain between 1 and 256 characters.", nameof(loginId));
        }

        passwordHash ??= string.Empty;
        if (passwordHash.Length > 256)
        {
            throw new ArgumentException("Password hash cannot exceed 256 characters.", nameof(passwordHash));
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO accounts (m_id, password_hash)
                VALUES (@loginId, @passwordHash)
                ON CONFLICT(m_id) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("@loginId", loginId);
            insert.Parameters.AddWithValue("@passwordHash", passwordHash);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        AccountRecord account;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = (SqliteTransaction)transaction;
            select.CommandText = """
                SELECT account_id, m_id, password_hash, created_at
                FROM accounts
                WHERE m_id = @loginId;
                """;
            select.Parameters.AddWithValue("@loginId", loginId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"Account '{loginId}' could not be resolved after creation.");
            }

            account = new AccountRecord(
                new AccountId(reader.GetInt32(0)),
                reader.GetString(1),
                reader.GetString(2),
                ParseCreatedAt(reader.GetString(3)));
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return account;
    }

    private void EnsureSchema()
    {
        CharacterDatabaseSchema.Ensure(connectionString);
    }

    private static DateTimeOffset ParseCreatedAt(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var createdAt)
            ? createdAt
            : throw new InvalidOperationException($"Account created_at value '{value}' is invalid.");
}
