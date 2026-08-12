using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Characters;

internal sealed class SqliteCharacterEntryMutationRepository
    : ICharacterEntryMutationRepository, ISelectedSessionPositionPersistence
{
    private readonly string connectionString;

    public SqliteCharacterEntryMutationRepository(string databasePath)
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath) }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        EnsurePositionColumns(connection);
    }

    public ValueTask PersistAsync(
        AccountId accountId, CharacterId characterId, CharacterPositionState position,
        CancellationToken cancellationToken) =>
        PersistPositionAsync(accountId, characterId, position, cancellationToken);

    public async ValueTask<IReadOnlyList<SeekingQuestTriggerUpdate>> UpdateSeekingQuestTriggersAsync(
        AccountId accountId, CharacterId characterId, IReadOnlyList<SeekingQuestTriggerUpdate> updates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updates);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await VerifyOwnerAsync(connection, transaction, accountId, characterId, cancellationToken);
        var committed = new List<SeekingQuestTriggerUpdate>();
        foreach (var update in updates.OrderBy(value => value.Slot))
        {
            if (update.After == update.Before) continue;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE character_active_quests SET trigger_value=@after
                WHERE character_id=@cid AND slot=@slot AND quest_id=@qid AND trigger_value=@before
                """;
            command.Parameters.AddWithValue("@after", update.After);
            command.Parameters.AddWithValue("@cid", characterId.Value);
            command.Parameters.AddWithValue("@slot", update.Slot);
            command.Parameters.AddWithValue("@qid", update.QuestId);
            command.Parameters.AddWithValue("@before", update.Before);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException($"Seeking quest {update.QuestId} changed concurrently.");
            committed.Add(update);
        }
        await transaction.CommitAsync(cancellationToken);
        return committed;
    }

    public async ValueTask<PetRevivalMutation?> ReviveEquippedCreatureAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await VerifyOwnerAsync(connection, transaction, accountId, characterId, cancellationToken);
        long creatureKey = 0;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT item_core FROM character_new_items WHERE character_id=@cid AND list_type=3 AND slot_index=24 LIMIT 1";
            command.Parameters.AddWithValue("@cid", characterId.Value);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is byte[] core && core.Length == 82 && core[0] == 5)
                creatureKey = BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5, 4));
        }
        if (creatureKey <= 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        int? before;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT field04 FROM character_creatures WHERE character_id=@cid AND creature_key=@key LIMIT 1";
            command.Parameters.AddWithValue("@cid", characterId.Value);
            command.Parameters.AddWithValue("@key", creatureKey);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            before = value is null || value is DBNull ? null : Convert.ToInt32(value);
        }
        if (before is null || before > 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE character_creatures SET field04=1 WHERE character_id=@cid AND creature_key=@key AND field04<=0";
            command.Parameters.AddWithValue("@cid", characterId.Value);
            command.Parameters.AddWithValue("@key", creatureKey);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException("Equipped creature revival changed concurrently.");
        }
        await transaction.CommitAsync(cancellationToken);
        return new PetRevivalMutation(creatureKey, Math.Clamp(before.Value, 0, byte.MaxValue), 1);
    }

    public async ValueTask PersistPositionAsync(
        AccountId accountId, CharacterId characterId, CharacterPositionState position,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE characters SET town_id=@town,area_id=@area,pos_x=@x,pos_y=@y,direction=@direction,area_state=3
            WHERE character_id=@cid AND account_id=@aid AND delete_flag=0
            """;
        command.Parameters.AddWithValue("@town", position.TownId);
        command.Parameters.AddWithValue("@area", position.AreaId);
        command.Parameters.AddWithValue("@x", position.X);
        command.Parameters.AddWithValue("@y", position.Y);
        command.Parameters.AddWithValue("@direction", position.Direction);
        command.Parameters.AddWithValue("@cid", characterId.Value);
        command.Parameters.AddWithValue("@aid", accountId.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidDataException("Entry position owner is unavailable.");
    }

    private async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async ValueTask VerifyOwnerAsync(SqliteConnection connection, SqliteTransaction transaction,
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM characters WHERE character_id=@cid AND account_id=@aid AND delete_flag=0";
        command.Parameters.AddWithValue("@cid", characterId.Value);
        command.Parameters.AddWithValue("@aid", accountId.Value);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            throw new InvalidDataException("Entry mutation character ownership does not match.");
    }

    private static void EnsurePositionColumns(SqliteConnection connection)
    {
        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(characters)";
        using var reader = columns.ExecuteReader();
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) present.Add(reader.GetString(1));
        reader.Close();
        foreach (var (name, declaration) in new[]
        {
            ("town_id", "INTEGER NOT NULL DEFAULT 0"), ("area_id", "INTEGER NOT NULL DEFAULT 0"),
            ("pos_x", "INTEGER NOT NULL DEFAULT 0"), ("pos_y", "INTEGER NOT NULL DEFAULT 0"),
            ("direction", "INTEGER NOT NULL DEFAULT 5"), ("area_state", "INTEGER NOT NULL DEFAULT 3"),
        })
        {
            if (present.Contains(name)) continue;
            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE characters ADD COLUMN {name} {declaration}";
            alter.ExecuteNonQuery();
        }
    }
}
