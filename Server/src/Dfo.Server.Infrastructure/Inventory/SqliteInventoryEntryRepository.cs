using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Inventory;

internal sealed class SqliteInventoryEntryRepository : IInventoryEntryRepository
{
    private const int ItemCoreSize = 82;
    private readonly string connectionString;

    public SqliteInventoryEntryRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        EnsureSchema();
    }

    public async ValueTask<InventoryEntryStorageRecord> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        if (accountId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Account ID must be positive.");
        }

        if (characterId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), "Character ID must be positive.");
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await VerifyCharacterOwnershipAsync(connection, accountId, characterId, cancellationToken)
            .ConfigureAwait(false);

        var characterItems = await LoadCharacterItemsAsync(connection, characterId, cancellationToken)
            .ConfigureAwait(false);
        var accountCargoItems = await LoadAccountCargoItemsAsync(connection, accountId, cancellationToken)
            .ConfigureAwait(false);
        await RejectUnmigratedLegacyInventoryAsync(
            connection,
            accountId,
            characterId,
            characterItems.Count,
            accountCargoItems.Count,
            cancellationToken).ConfigureAwait(false);
        var containerParameters = await LoadContainerParametersAsync(
            connection,
            characterId,
            cancellationToken).ConfigureAwait(false);
        var cargoState = await LoadAccountCargoStateAsync(connection, accountId, cancellationToken)
            .ConfigureAwait(false);
        var characterVirtualResources = LoadCharacterVirtualResources(characterItems);
        var accountVirtualResources = await LoadAccountVirtualResourcesAsync(
            connection,
            accountId,
            cancellationToken).ConfigureAwait(false);
        var itemValues = await LoadItemValuesAsync(connection, characterId, cancellationToken)
            .ConfigureAwait(false);
        var equipmentLocks = await LoadEquipmentLocksAsync(connection, characterId, cancellationToken)
            .ConfigureAwait(false);
        var rentals = await LoadRentalsAsync(connection, characterId, cancellationToken)
            .ConfigureAwait(false);
        var goldLimits = await LoadGoldLimitsAsync(connection, characterId, cancellationToken)
            .ConfigureAwait(false);

        ValidateUniqueSlots(characterItems, "character inventory");
        ValidateUniqueSlots(accountCargoItems, "account cargo");
        return new InventoryEntryStorageRecord(
            accountId,
            characterId,
            characterItems,
            accountCargoItems,
            containerParameters,
            cargoState,
            characterVirtualResources,
            accountVirtualResources,
            itemValues,
            equipmentLocks,
            rentals,
            goldLimits);
    }

    private static async ValueTask VerifyCharacterOwnershipAsync(
        SqliteConnection connection,
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM characters
            WHERE character_id = @characterId AND account_id = @accountId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@characterId", characterId.Value);
        command.Parameters.AddWithValue("@accountId", accountId.Value);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (found is null or DBNull)
        {
            throw new InvalidDataException(
                $"Character {characterId.Value} does not belong to account {accountId.Value}.");
        }
    }

    private static async ValueTask<IReadOnlyList<InventoryStorageItem>> LoadCharacterItemsAsync(
        SqliteConnection connection,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var items = new List<InventoryStorageItem>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT item_uid, owner_scope, owner_id, character_id,
                   list_type, slot_index, item_core
            FROM character_new_items
            WHERE character_id = @characterId
            ORDER BY list_type, slot_index, item_uid;
            """;
        command.Parameters.AddWithValue("@characterId", characterId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var ownerType = ParseOwnerType(reader.GetString(1));
            var ownerId = reader.GetInt32(2);
            var storedCharacterId = reader.IsDBNull(3)
                ? (CharacterId?)null
                : new CharacterId(reader.GetInt32(3));
            var containerType = ParseContainerType(reader.GetInt32(4), allowAccountCargo: false);
            var slotIndex = reader.GetInt32(5);
            if (ownerType != InventoryOwnerType.Character
                || ownerId != characterId.Value
                || storedCharacterId != characterId)
            {
                throw new InvalidDataException(
                    $"Inventory row {reader.GetInt64(0)} has invalid character ownership.");
            }

            ValidateSlot(slotIndex, reader.GetInt64(0));
            items.Add(new InventoryStorageItem(
                reader.GetInt64(0),
                ownerType,
                ownerId,
                storedCharacterId,
                containerType,
                slotIndex,
                DecodeItemCore(ReadCore(reader, 6, reader.GetInt64(0)))));
        }

        return items;
    }

    private static async ValueTask<IReadOnlyList<InventoryStorageItem>> LoadAccountCargoItemsAsync(
        SqliteConnection connection,
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        var items = new List<InventoryStorageItem>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT item_uid, account_id, character_id, list_type, slot_index, item_core
            FROM account_cargo_new_items
            WHERE account_id = @accountId
            ORDER BY list_type, slot_index, item_uid;
            """;
        command.Parameters.AddWithValue("@accountId", accountId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var storageEntryId = reader.GetInt64(0);
            var ownerId = reader.GetInt32(1);
            var storedCharacterId = reader.IsDBNull(2)
                ? (CharacterId?)null
                : new CharacterId(reader.GetInt32(2));
            var containerType = ParseContainerType(reader.GetInt32(3), allowAccountCargo: true);
            var slotIndex = reader.GetInt32(4);
            if (ownerId != accountId.Value || containerType != InventoryContainerType.AccountCargo)
            {
                throw new InvalidDataException(
                    $"Inventory row {storageEntryId} has invalid account cargo ownership.");
            }

            ValidateSlot(slotIndex, storageEntryId);
            items.Add(new InventoryStorageItem(
                storageEntryId,
                InventoryOwnerType.Account,
                ownerId,
                storedCharacterId,
                containerType,
                slotIndex,
                DecodeItemCore(ReadCore(reader, 5, storageEntryId))));
        }

        return items;
    }

    private static async ValueTask<IReadOnlyList<InventoryContainerParameter>> LoadContainerParametersAsync(
        SqliteConnection connection,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var parameters = new List<InventoryContainerParameter>();
        var seen = new HashSet<InventoryContainerType>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT character_id, list_type, list_param16
            FROM character_container_state
            WHERE character_id = @characterId
            ORDER BY list_type;
            """;
        command.Parameters.AddWithValue("@characterId", characterId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var storedCharacterId = new CharacterId(reader.GetInt32(0));
            var containerType = ParseContainerType(reader.GetInt32(1), allowAccountCargo: false);
            if (storedCharacterId != characterId || !seen.Add(containerType))
            {
                throw new InvalidDataException("Character container metadata is duplicated or has the wrong owner.");
            }

            parameters.Add(new InventoryContainerParameter(
                storedCharacterId,
                containerType,
                ReadUInt16(reader, 2, "container parameter")));
        }

        return parameters;
    }

    private static async ValueTask<AccountCargoStorageState> LoadAccountCargoStateAsync(
        SqliteConnection connection,
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT account_id, selection_key, value32, item_count
            FROM account_cargo_state
            WHERE account_id = @accountId
            LIMIT 2;
            """;
        command.Parameters.AddWithValue("@accountId", accountId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new AccountCargoStorageState(accountId, 0, 0, 0);
        }

        var storedAccountId = new AccountId(reader.GetInt32(0));
        if (storedAccountId != accountId)
        {
            throw new InvalidDataException("Account cargo metadata has the wrong owner.");
        }

        var result = new AccountCargoStorageState(
            storedAccountId,
            ReadUInt16(reader, 1, "account cargo selection key"),
            reader.GetInt32(2),
            ReadUInt16(reader, 3, "account cargo item count"));
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("Account cargo metadata is duplicated.");
        }

        return result;
    }

    private static async ValueTask<InventoryGoldLimitState> LoadGoldLimitsAsync(
        SqliteConnection connection,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT gold_carry_limit, auction_gold_limit
            FROM character_gold_limits
            WHERE character_id = @characterId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@characterId", characterId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new InventoryGoldLimitState(0, 0);
        }

        var carryLimit = reader.GetInt32(0);
        var auctionLimit = reader.GetInt32(1);
        if (carryLimit < 0 || auctionLimit < 0)
        {
            throw new InvalidDataException("Character gold limits cannot be negative.");
        }

        return new InventoryGoldLimitState(carryLimit, auctionLimit);
    }

    private static IReadOnlyList<InventoryRuntimeValueState> LoadCharacterVirtualResources(
        IReadOnlyList<InventoryStorageItem> characterItems)
    {
        return characterItems
            .Where(item => item.ContainerType == InventoryContainerType.Main
                && item.SlotIndex is >= 0 and <= 2)
            .Select(item => new InventoryRuntimeValueState(
                item.SlotIndex,
                item.Core.ItemId,
                Math.Max(0, item.Core.Value)))
            .OrderBy(value => value.SlotIndex)
            .ToArray();
    }

    private static async ValueTask<IReadOnlyList<InventoryRuntimeValueState>>
        LoadAccountVirtualResourcesAsync(
            SqliteConnection connection,
            AccountId accountId,
            CancellationToken cancellationToken)
    {
        var values = new List<InventoryRuntimeValueState>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT cube_black, cube_white, cube_red, cube_blue, cube_clear, cube_gold
            FROM accounts
            WHERE account_id = @accountId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@accountId", accountId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException($"Account {accountId.Value} is missing while loading inventory values.");
        }

        var itemIds = new[] { 3033, 3034, 3035, 3036, 3037, 3262 };
        for (var index = 0; index < itemIds.Length; index++)
        {
            var count = reader.GetInt32(index);
            if (count < 0)
            {
                throw new InvalidDataException("Account cube fragment counts cannot be negative.");
            }

            if (count > 0)
            {
                values.Add(new InventoryRuntimeValueState(354 + index, itemIds[index], count));
            }
        }

        return values.OrderBy(value => value.SlotIndex).ToArray();
    }

    private static async ValueTask<IReadOnlyList<InventoryItemValueStorageRecord>> LoadItemValuesAsync(
        SqliteConnection connection,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var values = new List<InventoryItemValueStorageRecord>();
        var seen = new HashSet<(InventoryItemValueListType ListType, int SortOrder)>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT character_id, list_kind, sort_order, item_id, value
            FROM character_item_values
            WHERE character_id = @characterId
            ORDER BY CASE list_kind WHEN 'cooltime' THEN 0 WHEN 'effect' THEN 1 ELSE 2 END,
                     sort_order;
            """;
        command.Parameters.AddWithValue("@characterId", characterId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var storedCharacterId = new CharacterId(reader.GetInt32(0));
            var listType = reader.GetString(1) switch
            {
                "cooltime" => InventoryItemValueListType.Cooldown,
                "effect" => InventoryItemValueListType.Effect,
                var value => throw new InvalidDataException(
                    $"Character item value list kind '{value}' is invalid."),
            };
            var sortOrder = reader.GetInt32(2);
            if (storedCharacterId != characterId
                || sortOrder < 0
                || !seen.Add((listType, sortOrder)))
            {
                throw new InvalidDataException(
                    "Character item values have invalid ownership, order, or duplicate entries.");
            }

            values.Add(new InventoryItemValueStorageRecord(
                storedCharacterId,
                listType,
                sortOrder,
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }

        return values;
    }

    private static async ValueTask<IReadOnlyList<InventoryEquipmentLockStorageRecord>>
        LoadEquipmentLocksAsync(
            SqliteConnection connection,
            CharacterId characterId,
            CancellationToken cancellationToken)
    {
        var locks = new List<InventoryEquipmentLockStorageRecord>();
        var lockIds = new HashSet<int>();
        var slots = new HashSet<(InventoryContainerType ContainerType, int SlotIndex)>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT character_id, equipment_lock_id, inventory_list_type,
                   slot, state, COALESCE(remaining_seconds, 0)
            FROM character_item_locks
            WHERE character_id = @characterId
            ORDER BY equipment_lock_id;
            """;
        command.Parameters.AddWithValue("@characterId", characterId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var storedCharacterId = new CharacterId(reader.GetInt32(0));
            var equipmentLockId = reader.GetInt32(1);
            var containerType = ParseLockContainerType(reader.GetInt32(2));
            var slotIndex = reader.GetInt32(3);
            var state = reader.GetInt32(4);
            var remainingSeconds = reader.GetInt32(5);
            if (storedCharacterId != characterId
                || equipmentLockId is < 1 or > byte.MaxValue
                || slotIndex < 0
                || state is < byte.MinValue or > byte.MaxValue
                || remainingSeconds < 0
                || !lockIds.Add(equipmentLockId)
                || !slots.Add((containerType, slotIndex)))
            {
                throw new InvalidDataException(
                    "Character equipment locks have invalid ownership, identity, slot, or runtime state.");
            }

            locks.Add(new InventoryEquipmentLockStorageRecord(
                storedCharacterId,
                equipmentLockId,
                containerType,
                slotIndex,
                state,
                remainingSeconds));
        }

        return locks;
    }

    private static async ValueTask<IReadOnlyList<CharacterRentalStorageRecord>> LoadRentalsAsync(
        SqliteConnection connection,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var rentals = new List<CharacterRentalStorageRecord>();
        var itemIds = new HashSet<long>();
        var inventoryTemplateIds = new HashSet<long>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT character_id, shop_entry_id, inventory_template_id, expire_time
            FROM character_rental_items
            WHERE character_id = @characterId
            UNION ALL
            SELECT character_id, item_id, inventory_template_id, expire_time
            FROM rental_items
            WHERE character_id = @characterId;
            """;
        command.Parameters.AddWithValue("@characterId", characterId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var storedCharacterId = new CharacterId(reader.GetInt32(0));
            var itemId = reader.GetInt64(1);
            var inventoryTemplateId = reader.GetInt64(2);
            var expireTime = reader.GetInt64(3);
            if (storedCharacterId != characterId
                || itemId is < 1 or > uint.MaxValue
                || inventoryTemplateId is < 0 or > uint.MaxValue
                || expireTime is < 1 or > uint.MaxValue
                || !itemIds.Add(itemId)
                || (inventoryTemplateId != 0 && !inventoryTemplateIds.Add(inventoryTemplateId)))
            {
                throw new InvalidDataException(
                    "Character rental state has invalid ownership, identity, expiration, or duplicates.");
            }

            rentals.Add(new CharacterRentalStorageRecord(
                storedCharacterId,
                itemId,
                inventoryTemplateId,
                expireTime));
        }

        return rentals
            .OrderBy(item => item.ItemId)
            .ThenBy(item => item.InventoryTemplateId)
            .ToArray();
    }

    private static async ValueTask RejectUnmigratedLegacyInventoryAsync(
        SqliteConnection connection,
        AccountId accountId,
        CharacterId characterId,
        int characterNewItemCount,
        int accountCargoNewItemCount,
        CancellationToken cancellationToken)
    {
        if (characterNewItemCount == 0)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT EXISTS(
                    SELECT 1
                    FROM character_items
                    WHERE character_id = @characterId
                       OR (owner_scope = 'character' AND owner_id = @characterId));
                """;
            command.Parameters.AddWithValue("@characterId", characterId.Value);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
            {
                throw new InvalidDataException(
                    $"Legacy character inventory exists for character {characterId.Value}; migration required before entry loading.");
            }
        }

        if (accountCargoNewItemCount == 0)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT EXISTS(
                    SELECT 1
                    FROM account_cargo_items
                    WHERE account_id = @accountId);
                """;
            command.Parameters.AddWithValue("@accountId", accountId.Value);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
            {
                throw new InvalidDataException(
                    $"Legacy account cargo exists for account {accountId.Value}; migration required before entry loading.");
            }
        }
    }

    private static InventoryOwnerType ParseOwnerType(string value) => value switch
    {
        "character" => InventoryOwnerType.Character,
        "account" => InventoryOwnerType.Account,
        _ => throw new InvalidDataException($"Inventory owner scope '{value}' is invalid."),
    };

    private static InventoryContainerType ParseContainerType(int value, bool allowAccountCargo)
    {
        var result = value switch
        {
            0 => InventoryContainerType.Main,
            1 => InventoryContainerType.Avatar,
            2 => InventoryContainerType.PersonalCargo,
            3 => InventoryContainerType.Equipment,
            7 => InventoryContainerType.Pet,
            12 when allowAccountCargo => InventoryContainerType.AccountCargo,
            _ => throw new InvalidDataException($"Inventory container type {value} is not an entry container."),
        };
        return result;
    }

    private static InventoryContainerType ParseLockContainerType(int value) => value switch
    {
        0 => InventoryContainerType.Main,
        1 => InventoryContainerType.Avatar,
        2 => InventoryContainerType.PersonalCargo,
        3 => InventoryContainerType.Equipment,
        7 => InventoryContainerType.Pet,
        12 => InventoryContainerType.AccountCargo,
        19 => InventoryContainerType.TitleBookGeneral,
        20 => InventoryContainerType.TitleBookSpecific,
        21 => InventoryContainerType.TitleBookPvp,
        22 => InventoryContainerType.TitleBookDespair,
        23 => InventoryContainerType.TitleBookEvent,
        29 => InventoryContainerType.QuickSlot,
        33 => InventoryContainerType.KnightShieldEquipped,
        34 => InventoryContainerType.KnightShieldCatalog,
        _ => throw new InvalidDataException($"Equipment lock container type {value} is invalid."),
    };

    private static byte[] ReadCore(SqliteDataReader reader, int ordinal, long storageEntryId)
    {
        if (reader.IsDBNull(ordinal))
        {
            throw new InvalidDataException($"Inventory row {storageEntryId} has no item core.");
        }

        var core = (byte[])reader.GetValue(ordinal);
        if (core.Length != ItemCoreSize)
        {
            throw new InvalidDataException(
                $"Inventory row {storageEntryId} item core length is {core.Length}; expected {ItemCoreSize}.");
        }

        return core;
    }

    private static InventoryItemCoreState DecodeItemCore(ReadOnlySpan<byte> core) => new(
        core[0],
        BinaryPrimitives.ReadInt32LittleEndian(core[1..]),
        BinaryPrimitives.ReadInt32LittleEndian(core[5..]),
        core[9],
        BinaryPrimitives.ReadUInt16LittleEndian(core[10..]),
        core[12],
        BinaryPrimitives.ReadInt32LittleEndian(core[13..]),
        core[17],
        core[18],
        BinaryPrimitives.ReadUInt16LittleEndian(core[19..]),
        BinaryPrimitives.ReadInt32LittleEndian(core[21..]),
        DecodeChronicleOption(core[25..]),
        DecodeChronicleOption(core[33..]),
        BinaryPrimitives.ReadInt32LittleEndian(core[41..]),
        core[45],
        BinaryPrimitives.ReadInt32LittleEndian(core[46..]),
        BinaryPrimitives.ReadInt32LittleEndian(core[50..]),
        BinaryPrimitives.ReadUInt16LittleEndian(core[54..]),
        DecodeRandomOption(core[56..]),
        DecodeRandomOption(core[59..]),
        DecodeRandomOption(core[62..]),
        core[65],
        core[66],
        core[67],
        DecodeRandomOption(core[68..]),
        core[71],
        core[72],
        core[73],
        BinaryPrimitives.ReadUInt16LittleEndian(core[74..]),
        core[76],
        core[77],
        core[78],
        core[79],
        core[80],
        core[81]);

    private static ChronicleOptionState DecodeChronicleOption(ReadOnlySpan<byte> option) => new(
        BinaryPrimitives.ReadInt32LittleEndian(option),
        option[4],
        option[5],
        option[6],
        option[7]);

    private static RandomOptionState DecodeRandomOption(ReadOnlySpan<byte> option) =>
        new(option[0], option[1], option[2]);

    private static ushort ReadUInt16(SqliteDataReader reader, int ordinal, string name)
    {
        var value = reader.GetInt32(ordinal);
        if (value is < ushort.MinValue or > ushort.MaxValue)
        {
            throw new InvalidDataException($"Stored {name} {value} is outside the unsigned 16-bit range.");
        }

        return (ushort)value;
    }

    private static void ValidateSlot(int slotIndex, long storageEntryId)
    {
        if (slotIndex < 0)
        {
            throw new InvalidDataException($"Inventory row {storageEntryId} has negative slot {slotIndex}.");
        }
    }

    private static void ValidateUniqueSlots(
        IReadOnlyList<InventoryStorageItem> items,
        string source)
    {
        var seen = new HashSet<(InventoryContainerType ContainerType, int SlotIndex)>();
        foreach (var item in items)
        {
            if (!seen.Add((item.ContainerType, item.SlotIndex)))
            {
                throw new InvalidDataException(
                    $"The {source} contains duplicate slot {item.ContainerType}/{item.SlotIndex}.");
            }
        }
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS character_new_items (
                item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
                owner_scope TEXT NOT NULL CHECK (owner_scope IN ('character', 'account')),
                owner_id INTEGER NOT NULL,
                character_id INTEGER,
                list_type INTEGER NOT NULL,
                slot_index INTEGER NOT NULL,
                item_core BLOB NOT NULL CHECK(length(item_core) = 82),
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(owner_scope, owner_id, list_type, slot_index)
            );

            CREATE INDEX IF NOT EXISTS idx_character_new_items_character_space
                ON character_new_items(character_id, list_type, slot_index);

            CREATE TABLE IF NOT EXISTS account_cargo_new_items (
                item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id INTEGER NOT NULL,
                character_id INTEGER,
                list_type INTEGER NOT NULL,
                slot_index INTEGER NOT NULL,
                item_core BLOB NOT NULL CHECK(length(item_core) = 82),
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(account_id, slot_index)
            );

            CREATE TABLE IF NOT EXISTS character_container_state (
                character_id INTEGER NOT NULL,
                list_type INTEGER NOT NULL,
                list_param16 INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (character_id, list_type)
            );

            CREATE TABLE IF NOT EXISTS account_cargo_state (
                account_id INTEGER PRIMARY KEY,
                selection_key INTEGER NOT NULL DEFAULT 0,
                value32 INTEGER NOT NULL DEFAULT 0,
                item_count INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS character_gold_limits (
                character_id INTEGER PRIMARY KEY,
                gold_carry_limit INTEGER NOT NULL,
                auction_gold_limit INTEGER NOT NULL,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS character_item_values (
                character_id INTEGER NOT NULL,
                list_kind TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                item_id INTEGER NOT NULL,
                value INTEGER NOT NULL,
                PRIMARY KEY (character_id, list_kind, sort_order)
            );

            CREATE TABLE IF NOT EXISTS character_item_locks (
                character_id INTEGER NOT NULL,
                equipment_lock_id INTEGER NOT NULL,
                inventory_list_type INTEGER NOT NULL,
                slot INTEGER NOT NULL,
                state INTEGER NOT NULL,
                remaining_seconds INTEGER,
                PRIMARY KEY (character_id, equipment_lock_id)
            );

            CREATE TABLE IF NOT EXISTS character_rental_items (
                character_id INTEGER NOT NULL,
                shop_entry_id INTEGER NOT NULL,
                inventory_template_id INTEGER NOT NULL DEFAULT 0,
                expire_time INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_character_rental_items_char
                ON character_rental_items(character_id);

            CREATE TABLE IF NOT EXISTS rental_items (
                character_id INTEGER NOT NULL,
                item_id INTEGER NOT NULL,
                inventory_template_id INTEGER NOT NULL DEFAULT 0,
                expire_time INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_rental_items_char
                ON rental_items(character_id);

            CREATE TABLE IF NOT EXISTS character_items (
                item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
                owner_scope TEXT NOT NULL CHECK (owner_scope IN ('character', 'account')),
                owner_id INTEGER NOT NULL,
                character_id INTEGER,
                list_type INTEGER NOT NULL,
                slot_index INTEGER NOT NULL,
                item_template_id INTEGER NOT NULL,
                item_kind TEXT NOT NULL DEFAULT 'unknown',
                stack_count INTEGER NOT NULL DEFAULT 0,
                instance_value INTEGER NOT NULL DEFAULT 0,
                durability INTEGER NOT NULL DEFAULT 0,
                seal_flag INTEGER NOT NULL DEFAULT 0,
                option_value INTEGER NOT NULL DEFAULT 0,
                equipment_lock_id INTEGER NOT NULL DEFAULT 0,
                expire_time INTEGER NOT NULL DEFAULT 0,
                marker_16 INTEGER NOT NULL DEFAULT 0,
                pet_serial_or_handle INTEGER NOT NULL DEFAULT 0,
                extra_json TEXT NOT NULL DEFAULT '{}',
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_character_items_character
                ON character_items(character_id, list_type, slot_index);

            CREATE TABLE IF NOT EXISTS account_cargo_items (
                item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id INTEGER NOT NULL,
                slot_index INTEGER NOT NULL,
                item_template_id INTEGER NOT NULL,
                item_kind TEXT NOT NULL DEFAULT 'unknown',
                stack_count INTEGER NOT NULL DEFAULT 0,
                instance_value INTEGER NOT NULL DEFAULT 0,
                durability INTEGER NOT NULL DEFAULT 0,
                seal_flag INTEGER NOT NULL DEFAULT 0,
                option_value INTEGER NOT NULL DEFAULT 0,
                expire_time INTEGER NOT NULL DEFAULT 0,
                marker_16 INTEGER NOT NULL DEFAULT 0,
                extra_json TEXT NOT NULL DEFAULT '{}',
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_account_cargo_items_owner
                ON account_cargo_items(account_id, slot_index);

            CREATE TABLE IF NOT EXISTS accounts (
                account_id INTEGER PRIMARY KEY AUTOINCREMENT,
                m_id TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL DEFAULT '',
                last_login_ip TEXT NOT NULL DEFAULT '',
                last_login_at TEXT,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                cube_black INTEGER NOT NULL DEFAULT 0,
                cube_white INTEGER NOT NULL DEFAULT 0,
                cube_red INTEGER NOT NULL DEFAULT 0,
                cube_blue INTEGER NOT NULL DEFAULT 0,
                cube_clear INTEGER NOT NULL DEFAULT 0,
                cube_gold INTEGER NOT NULL DEFAULT 0
            );
            """;
        command.ExecuteNonQuery();
        EnsureAccountCubeColumns(connection);
    }

    private static void EnsureAccountCubeColumns(SqliteConnection connection)
    {
        foreach (var column in new[]
        {
            "cube_black", "cube_white", "cube_red", "cube_blue", "cube_clear", "cube_gold",
        })
        {
            using var inspect = connection.CreateCommand();
            inspect.CommandText = "SELECT COUNT(*) FROM pragma_table_info('accounts') WHERE name = @name;";
            inspect.Parameters.AddWithValue("@name", column);
            if (Convert.ToInt32(inspect.ExecuteScalar()) != 0)
            {
                continue;
            }

            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE accounts ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0;";
            alter.ExecuteNonQuery();
        }
    }
}
