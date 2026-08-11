using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Characters;

internal sealed class SqliteCharacterRepository : ICharacterRepository
{
    private readonly string connectionString;

    public SqliteCharacterRepository(string databasePath)
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

    public async ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        if (accountId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Account ID must be positive.");
        }

        var characters = new List<CharacterSummary>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT character_id,
                   slot_index,
                   COALESCE(name_bytes, CAST(name AS BLOB)),
                   job,
                   grow_type,
                   level,
                   clone_title_item_id
            FROM characters
            WHERE account_id = @accountId AND delete_flag = 0
            ORDER BY slot_index, character_id;
            """;
        command.Parameters.AddWithValue("@accountId", accountId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            characters.Add(new CharacterSummary(
                new CharacterId(reader.GetInt32(0)),
                checked((ushort)reader.GetInt32(1)),
                reader.IsDBNull(2) ? [] : (byte[])reader.GetValue(2),
                checked((byte)reader.GetInt32(3)),
                checked((byte)reader.GetInt32(4)),
                checked((byte)reader.GetInt32(5)),
                reader.IsDBNull(6) ? 0u : checked((uint)reader.GetInt64(6))));
        }

        return characters;
    }

    public async ValueTask<ActiveCharacterState?> FindByAccountAndSlotAsync(
        AccountId accountId,
        ushort slotIndex,
        CancellationToken cancellationToken)
    {
        if (accountId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Account ID must be positive.");
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT character_id,
                   slot_index,
                   COALESCE(name_bytes, CAST(name AS BLOB)),
                   job,
                   grow_type,
                   level
            FROM characters
            WHERE account_id = @accountId
              AND slot_index = @slotIndex
              AND delete_flag = 0
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@accountId", accountId.Value);
        command.Parameters.AddWithValue("@slotIndex", slotIndex);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var characterId = new CharacterId(reader.GetInt32(0));
        return new ActiveCharacterState(
            new CharacterIdentityState(
                characterId,
                checked((ushort)reader.GetInt32(1)),
                reader.IsDBNull(2) ? [] : (byte[])reader.GetValue(2),
                checked((byte)reader.GetInt32(3)),
                checked((byte)reader.GetInt32(4))),
            new CharacterProgressionState(checked((byte)reader.GetInt32(5)), 0),
            new CharacterFatigueState(188, 0),
            new CharacterPositionState(0, 0, 0, 0, 5),
            DateTimeOffset.UnixEpoch,
            DarkKnightCombo: null);
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS characters (
                character_id INTEGER PRIMARY KEY,
                account_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                name_bytes BLOB,
                job INTEGER NOT NULL DEFAULT 0,
                grow_type INTEGER NOT NULL DEFAULT 0,
                level INTEGER NOT NULL DEFAULT 1,
                clone_title_item_id INTEGER NOT NULL DEFAULT 0,
                delete_flag INTEGER NOT NULL DEFAULT 0,
                slot_index INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_characters_account
                ON characters(account_id, delete_flag, slot_index);
            """;
        command.ExecuteNonQuery();
    }
}
