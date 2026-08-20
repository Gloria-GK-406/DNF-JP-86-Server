using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Infrastructure.Schema;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Accounts;

internal sealed class SqliteAccountEntryStateRepository : IAccountEntryStateRepository
{
    private const int SettingsMaximumBytes = 64 * 1024;
    private readonly string connectionString;

    public SqliteAccountEntryStateRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        EnsureSchema();
    }

    public async ValueTask<AccountEntryStorageRecord> LoadAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        if (accountId.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(accountId), "Account ID must be positive.");

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var wallet = await LoadWalletAsync(connection, transaction, accountId, cancellationToken)
            .ConfigureAwait(false);
        var settings = await LoadSettingsAsync(connection, transaction, accountId, cancellationToken)
            .ConfigureAwait(false);
        var premiums = await LoadPremiumsAsync(connection, transaction, accountId, cancellationToken)
            .ConfigureAwait(false);
        var roster = await LoadRosterAsync(connection, transaction, accountId, cancellationToken)
            .ConfigureAwait(false);
        var lottery = await LoadLotteryAsync(connection, transaction, accountId, cancellationToken)
            .ConfigureAwait(false);
        var relations = await LoadRelationsAsync(connection, transaction, accountId, cancellationToken)
            .ConfigureAwait(false);
        var unknown730 = await LoadUnknown730Async(connection, transaction, accountId, cancellationToken)
            .ConfigureAwait(false);
        var pcRoom = await LoadPcRoomStatesAsync(connection, transaction, accountId, cancellationToken)
            .ConfigureAwait(false);

        if (roster.Count == 0)
            throw new InvalidDataException($"Account {accountId.Value} has no selectable roster state.");
        if (pcRoom.Count != roster.Count || pcRoom.Distinct().Count() != 1)
            throw new InvalidDataException("Account PC-room compatibility state is missing or inconsistent.");

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AccountEntryStorageRecord(
            accountId,
            settings,
            new AccountWalletEntitlementStorageRecord(
                wallet.Cera, wallet.TokenCera, wallet.HappyTokenCera, wallet.LuckyStar, premiums),
            new AccountProgressionStorageRecord(
                wallet.HonorExperience, wallet.GrowthCapsuleExperience, roster, lottery),
            new AccountRelationCompatibilityStorageRecord(relations, unknown730, pcRoom));
    }

    public async ValueTask<AccountSettingsStorageRecord> LoadSettingsAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        if (accountId.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(accountId), "Account ID must be positive.");

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var settings = await LoadSettingsAsync(connection, transaction, accountId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return settings;
    }

    private static async ValueTask<(long Cera, long TokenCera, long HappyTokenCera, long LuckyStar,
        long HonorExperience, long GrowthCapsuleExperience)> LoadWalletAsync(
        SqliteConnection connection, SqliteTransaction transaction, AccountId accountId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT cera,token_cera,happy_token_cera,lucky_star,honor_exp,growth_capsule_exp
            FROM accounts WHERE account_id=@aid;
            """, accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException($"Required account {accountId.Value} is unavailable.");
        var values = (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
        if (values.Item1 is < 0 or > int.MaxValue || values.Item2 is < 0 or > int.MaxValue
            || values.Item3 is < 0 or > int.MaxValue || values.Item4 is < 0 or > ushort.MaxValue
            || values.Item5 < 0 || values.Item6 is < 0 or > uint.MaxValue)
            throw new InvalidDataException("Account wallet or progression value is out of range.");
        return values;
    }

    private static async ValueTask<AccountSettingsStorageRecord> LoadSettingsAsync(
        SqliteConnection connection, SqliteTransaction transaction, AccountId accountId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT main_game_option,quickchat_bank0,quickchat_bank1,hotkey_key_type,hotkey_slots
            FROM account_settings WHERE account_id=@aid;
            """, accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return new AccountSettingsStorageRecord(null, [], [], 0, []);
        var main = ReadOptionalBlob(reader, 0, "main game options");
        var bank0 = ReadBlob(reader, 1, "quick-chat bank 0", allowNull: true);
        var bank1 = ReadBlob(reader, 2, "quick-chat bank 1", allowNull: true);
        var hotkeyType = reader.GetInt32(3);
        var hotkeys = ReadBlob(reader, 4, "account hotkeys", allowNull: true);
        if (main is not null && (main.Length == 0 || main.Length % 2 != 0)
            || hotkeys.Length == 1
            || hotkeyType is < 0 or > byte.MaxValue)
            throw new InvalidDataException("Account settings are malformed.");
        var accountHotkeyPrefix = hotkeys.Length >= 2 ? hotkeys[..2] : [];
        return new AccountSettingsStorageRecord(main, bank0, bank1, hotkeyType, accountHotkeyPrefix);
    }

    private static async ValueTask<IReadOnlyList<(int PremiumType, long ExpiresAtUnixSeconds)>> LoadPremiumsAsync(
        SqliteConnection connection, SqliteTransaction transaction, AccountId accountId,
        CancellationToken cancellationToken)
    {
        var result = new List<(int, long)>();
        await using var command = CreateCommand(connection, transaction,
            "SELECT premium_type,end_time FROM account_premiums WHERE account_id=@aid ORDER BY premium_type", accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = (reader.GetInt32(0), reader.GetInt64(1));
            if (row.Item2 < 0)
                throw new InvalidDataException("Account premium state is out of range.");
            result.Add(row);
        }
        return result;
    }

    private static async ValueTask<IReadOnlyList<(int CharacterId, int SlotIndex, int Level)>> LoadRosterAsync(
        SqliteConnection connection, SqliteTransaction transaction, AccountId accountId,
        CancellationToken cancellationToken)
    {
        var result = new List<(int, int, int)>();
        await using var command = CreateCommand(connection, transaction, """
            SELECT character_id,slot_index,level FROM characters
            WHERE account_id=@aid AND delete_flag=0 ORDER BY slot_index,character_id
            """, accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
            if (row.Item1 <= 0 || row.Item2 is < 0 or > ushort.MaxValue || row.Item3 is < 0 or > byte.MaxValue)
                throw new InvalidDataException("Account roster state is out of range.");
            result.Add(row);
        }
        if (result.Select(value => value.Item2).Distinct().Count() != result.Count)
            throw new InvalidDataException("Account roster contains duplicate slots.");
        return result;
    }

    private static async ValueTask<IReadOnlyList<(int ItemTemplateId, int RewardIndex)>> LoadLotteryAsync(
        SqliteConnection connection, SqliteTransaction transaction, AccountId accountId,
        CancellationToken cancellationToken)
    {
        var result = new List<(int, int)>();
        await using var command = CreateCommand(connection, transaction, """
            SELECT item_template_id,reward_index FROM account_increase_chance_lottery_progress
            WHERE account_id=@aid ORDER BY item_template_id,reward_index
            """, accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = (reader.GetInt32(0), reader.GetInt32(1));
            if (row.Item1 <= 0 || row.Item2 is < 0 or >= 20)
                throw new InvalidDataException("Account lottery progress is out of range.");
            result.Add(row);
        }
        if (result.Distinct().Count() != result.Count)
            throw new InvalidDataException("Account lottery progress contains duplicates.");
        return result;
    }

    private static async ValueTask<IReadOnlyList<(int OwnerCharacterId, int Slot, int SupportCharacterId,
        int SkillId, int StrikerSkillId)>> LoadRelationsAsync(
        SqliteConnection connection, SqliteTransaction transaction, AccountId accountId,
        CancellationToken cancellationToken)
    {
        var result = new List<(int, int, int, int, int)>();
        await using var command = CreateCommand(connection, transaction, """
            SELECT r.owner_character_id,r.slot,r.support_character_id,r.skill_id,r.striker_skill_id,
                   support.account_id,support.delete_flag
            FROM character_mercenary_support r
            JOIN characters owner ON owner.character_id=r.owner_character_id
            LEFT JOIN characters support ON support.character_id=r.support_character_id
            WHERE owner.account_id=@aid AND owner.delete_flag=0
            ORDER BY owner.slot_index,r.slot
            """, accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(5) || reader.GetInt32(5) != accountId.Value || reader.GetInt32(6) != 0)
                throw new InvalidDataException("Account relation references a missing, deleted or foreign character.");
            var row = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetInt32(4));
            if (row.Item1 <= 0 || row.Item2 is < 0 or > byte.MaxValue || row.Item3 <= 0
                || row.Item4 <= 0 || row.Item5 < 0)
                throw new InvalidDataException("Account relation state is out of range.");
            result.Add(row);
        }
        return result;
    }

    private static async ValueTask<IReadOnlyList<(int CharacterSlotIndex, int SortOrder, int EntryId,
        int SentinelOrValue, int Flag)>> LoadUnknown730Async(
        SqliteConnection connection, SqliteTransaction transaction, AccountId accountId,
        CancellationToken cancellationToken)
    {
        var result = new List<(int, int, int, int, int)>();
        await using var command = CreateCommand(connection, transaction, """
            SELECT c.slot_index,u.sort_order,u.entry_id,u.sentinel_or_value,u.flag
            FROM character_buy_restrict_items u JOIN characters c ON c.character_id=u.character_id
            WHERE c.account_id=@aid AND c.delete_flag=0 ORDER BY c.slot_index,u.sort_order
            """, accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetInt32(4));
            if (row.Item1 is < 0 or > ushort.MaxValue || row.Item2 < 0)
                throw new InvalidDataException("Unknown730 compatibility ordering is invalid.");
            result.Add(row);
        }
        return result;
    }

    private static async ValueTask<IReadOnlyList<int>> LoadPcRoomStatesAsync(
        SqliteConnection connection, SqliteTransaction transaction, AccountId accountId,
        CancellationToken cancellationToken)
    {
        var result = new List<int>();
        await using var command = CreateCommand(connection, transaction, """
            SELECT f.pc_room_state FROM characters c
            LEFT JOIN character_init_flags f ON f.character_id=c.character_id
            WHERE c.account_id=@aid AND c.delete_flag=0 ORDER BY c.slot_index,c.character_id
            """, accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0))
                throw new InvalidDataException("PC-room compatibility state is unavailable.");
            var value = reader.GetInt32(0);
            if (value is < 0 or > byte.MaxValue)
                throw new InvalidDataException("PC-room compatibility state is out of range.");
            result.Add(value);
        }
        return result;
    }

    private static byte[] ReadBlob(SqliteDataReader reader, int ordinal, string name, bool allowNull)
    {
        if (reader.IsDBNull(ordinal))
        {
            if (allowNull) return [];
            throw new InvalidDataException($"Required {name} is unavailable.");
        }
        var value = (byte[])reader.GetValue(ordinal);
        if (value.Length > SettingsMaximumBytes)
            throw new InvalidDataException($"{name} exceeds the supported size.");
        return value;
    }

    private static byte[]? ReadOptionalBlob(SqliteDataReader reader, int ordinal, string name)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var value = (byte[])reader.GetValue(ordinal);
        if (value.Length > SettingsMaximumBytes)
            throw new InvalidDataException($"{name} exceeds the supported size.");
        return value;
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection, SqliteTransaction transaction, string sql, AccountId accountId)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@aid", accountId.Value);
        return command;
    }

    private void EnsureSchema()
    {
        CharacterDatabaseSchema.Ensure(connectionString);
    }
}
