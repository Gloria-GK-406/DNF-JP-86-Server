using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

public sealed class SqliteInventoryEntryRepositoryTests
{
    [Fact]
    public async Task LoadAsync_PreservesTypedItemsMetadataRuntimeValuesAndOwnership()
    {
        var databasePath = CreateDatabase();
        try
        {
            await SeedSchemaAsync(databasePath);
            var characterCore = CreateCore(itemKind: 1, itemId: 1001, value: 17);
            var cargoCore = CreateCore(itemKind: 2, itemId: 2002, value: 23);
            await ExecuteAsync(databasePath, """
                INSERT INTO characters(character_id, account_id) VALUES(21, 11);
                INSERT INTO accounts(account_id, cube_red) VALUES(11, 99);
                INSERT INTO character_new_items(
                    item_uid, owner_scope, owner_id, character_id, list_type, slot_index, item_core)
                VALUES(101, 'character', 21, 21, 3, 7, @characterCore);
                INSERT INTO account_cargo_new_items(
                    item_uid, account_id, character_id, list_type, slot_index, item_core)
                VALUES(202, 11, 21, 12, 4, @cargoCore);
                INSERT INTO character_container_state(character_id, list_type, list_param16)
                VALUES(21, 2, 48);
                INSERT INTO account_cargo_state(account_id, selection_key, value32, item_count)
                VALUES(11, 16, 3456, 1);
                INSERT INTO character_gold_limits(character_id, gold_carry_limit, auction_gold_limit)
                VALUES(21, 400000000, 500000000);
                INSERT INTO character_item_values(character_id, list_kind, sort_order, item_id, value)
                VALUES(21, 'cooltime', 2, 7002, 72),
                      (21, 'effect', 1, 8001, 81),
                      (21, 'cooltime', 0, 7000, 70);
                INSERT INTO character_item_locks(
                    character_id, equipment_lock_id, inventory_list_type, slot, state, remaining_seconds)
                VALUES(21, 9, 3, 7, 1, 0),
                      (21, 10, 12, 4, 2, 3600);
                INSERT INTO character_rental_items(
                    character_id, shop_entry_id, inventory_template_id, expire_time)
                VALUES(21, 9001, 1001, 1900000000);
                INSERT INTO rental_items(character_id, item_id, inventory_template_id, expire_time)
                VALUES(21, 9002, 1002, 1900000100);
                """,
                ("@characterCore", characterCore),
                ("@cargoCore", cargoCore));

            var repository = new InventorySide(databasePath).PerCall;
            var characterResult = await repository.LoadCharacterAsync(
                new AccountId(11),
                new CharacterId(21),
                CancellationToken.None);
            var accountResult = await repository.LoadAccountAsync(
                new AccountId(11),
                new CharacterId(21),
                CancellationToken.None);

            var characterItem = Assert.Single(characterResult.CharacterItems);
            Assert.Equal(InventoryOwnerType.Character, characterItem.OwnerType);
            Assert.Equal(21, characterItem.OwnerId);
            Assert.Equal(new CharacterId(21), characterItem.CharacterId);
            Assert.Equal(InventoryContainerType.Equipment, characterItem.ContainerType);
            Assert.Equal(7, characterItem.SlotIndex);
            AssertCore(characterItem.Core, 1001, 17);

            var cargoItem = Assert.Single(accountResult.AccountCargoItems);
            Assert.Equal(InventoryOwnerType.Account, cargoItem.OwnerType);
            Assert.Equal(11, cargoItem.OwnerId);
            Assert.Equal(InventoryContainerType.AccountCargo, cargoItem.ContainerType);
            Assert.Equal(4, cargoItem.SlotIndex);
            AssertCore(cargoItem.Core, 2002, 23);
            Assert.Equal((ushort)48, Assert.Single(characterResult.CharacterContainerParameters).Value);
            Assert.Equal((ushort)16, accountResult.AccountCargoState.SelectionKey);
            Assert.Equal(3456, accountResult.AccountCargoState.Value32);
            Assert.Equal((ushort)1, accountResult.AccountCargoState.StoredItemCount);
            Assert.Equal(
                new InventoryRuntimeValueState(356, 3035, 99),
                Assert.Single(accountResult.AccountVirtualResources));
            Assert.Empty(characterResult.CharacterVirtualResources);
            Assert.Collection(
                characterResult.CharacterItemValues,
                item => Assert.Equal(
                    new InventoryItemValueStorageRecord(
                        new CharacterId(21), InventoryItemValueListType.Cooldown, 0, 7000, 70),
                    item),
                item => Assert.Equal(
                    new InventoryItemValueStorageRecord(
                        new CharacterId(21), InventoryItemValueListType.Cooldown, 2, 7002, 72),
                    item),
                item => Assert.Equal(
                    new InventoryItemValueStorageRecord(
                        new CharacterId(21), InventoryItemValueListType.Effect, 1, 8001, 81),
                    item));
            Assert.Equal(
                [
                    new CharacterRentalStorageRecord(new CharacterId(21), 9001, 1001, 1900000000),
                    new CharacterRentalStorageRecord(new CharacterId(21), 9002, 1002, 1900000100),
                ],
                characterResult.CharacterRentals);
            Assert.Collection(
                characterResult.EquipmentLocks,
                item => Assert.Equal(
                    new InventoryEquipmentLockStorageRecord(
                        new CharacterId(21), 9, InventoryContainerType.Equipment, 7, 1, 0),
                    item));
            Assert.Collection(
                accountResult.EquipmentLocks,
                item => Assert.Equal(
                    new InventoryEquipmentLockStorageRecord(
                        new CharacterId(21), 10, InventoryContainerType.AccountCargo, 4, 2, 3600),
                    item));
            Assert.Equal(new InventoryGoldLimitState(400000000, 500000000), characterResult.GoldLimits);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsLoadedEmptyRecordWhenApplicableTablesHaveNoRows()
    {
        var databasePath = CreateDatabase();
        try
        {
            await SeedSchemaAsync(databasePath);
            await ExecuteAsync(databasePath,
                """
                INSERT INTO characters(character_id, account_id) VALUES(22, 12);
                INSERT INTO accounts(account_id) VALUES(12);
                """);

            var repository = new InventorySide(databasePath).PerCall;
            var characterResult = await repository.LoadCharacterAsync(
                new AccountId(12),
                new CharacterId(22),
                CancellationToken.None);
            var accountResult = await repository.LoadAccountAsync(
                new AccountId(12),
                new CharacterId(22),
                CancellationToken.None);

            Assert.Empty(characterResult.CharacterItems);
            Assert.Empty(accountResult.AccountCargoItems);
            Assert.Empty(characterResult.CharacterContainerParameters);
            Assert.Empty(characterResult.CharacterVirtualResources);
            Assert.Empty(accountResult.AccountVirtualResources);
            Assert.Empty(characterResult.CharacterItemValues);
            Assert.Empty(characterResult.EquipmentLocks);
            Assert.Empty(accountResult.EquipmentLocks);
            Assert.Empty(characterResult.CharacterRentals);
            Assert.Equal(new AccountCargoStorageState(new AccountId(12), 0, 0, 0), accountResult.AccountCargoState);
            Assert.Equal(new InventoryGoldLimitState(0, 0), characterResult.GoldLimits);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Theory]
    [InlineData("duplicate-item")]
    [InlineData("duplicate-template")]
    [InlineData("invalid-id")]
    [InlineData("invalid-expiry")]
    public async Task LoadAsync_RejectsInvalidRentalState(string invalidCase)
    {
        var databasePath = CreateDatabase();
        try
        {
            await SeedSchemaAsync(databasePath);
            await ExecuteAsync(databasePath, """
                INSERT INTO characters(character_id, account_id) VALUES(25, 15);
                INSERT INTO accounts(account_id) VALUES(15);
                """);
            var commandText = invalidCase switch
            {
                "duplicate-item" => """
                    INSERT INTO character_rental_items(
                        character_id, shop_entry_id, inventory_template_id, expire_time)
                    VALUES(25, 1, 101, 1000), (25, 1, 102, 1001);
                    """,
                "duplicate-template" => """
                    INSERT INTO character_rental_items(
                        character_id, shop_entry_id, inventory_template_id, expire_time)
                    VALUES(25, 1, 101, 1000), (25, 2, 101, 1001);
                    """,
                "invalid-id" => """
                    INSERT INTO character_rental_items(
                        character_id, shop_entry_id, inventory_template_id, expire_time)
                    VALUES(25, 0, 101, 1000);
                    """,
                _ => """
                    INSERT INTO character_rental_items(
                        character_id, shop_entry_id, inventory_template_id, expire_time)
                    VALUES(25, 1, 101, 0);
                    """,
            };
            await ExecuteAsync(databasePath, commandText);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new InventorySide(databasePath).PerCall.LoadCharacterAsync(
                    new AccountId(15),
                    new CharacterId(25),
                    CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Theory]
    [InlineData("character")]
    [InlineData("account")]
    public async Task LoadAsync_LegacyOnlyInventoryRequiresMigration(string ownerKind)
    {
        var databasePath = CreateDatabase();
        try
        {
            await SeedSchemaAsync(databasePath);
            await ExecuteAsync(databasePath, """
                INSERT INTO characters(character_id, account_id) VALUES(26, 16);
                INSERT INTO accounts(account_id) VALUES(16);
                """);
            if (ownerKind == "character")
            {
                await ExecuteAsync(databasePath, """
                    INSERT INTO character_items(
                        owner_scope, owner_id, character_id, list_type, slot_index, item_template_id)
                    VALUES('character', 26, 26, 0, 5, 100);
                    """);
            }
            else
            {
                await ExecuteAsync(databasePath, """
                    INSERT INTO account_cargo_items(account_id, slot_index, item_template_id)
                    VALUES(16, 5, 100);
                    """);
            }

            var repository = new InventorySide(databasePath).PerCall;
            var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                if (ownerKind == "character")
                {
                    await repository.LoadCharacterAsync(
                        new AccountId(16), new CharacterId(26), CancellationToken.None);
                }
                else
                {
                    await repository.LoadAccountAsync(
                        new AccountId(16), new CharacterId(26), CancellationToken.None);
                }
            });
            Assert.Contains("migration required", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Theory]
    [InlineData("duplicate-item-value")]
    [InlineData("duplicate-lock-id")]
    [InlineData("duplicate-lock-slot")]
    [InlineData("invalid-lock")]
    public async Task LoadAsync_RejectsInvalidAuxiliaryInventoryState(string invalidCase)
    {
        var databasePath = CreateDatabase();
        try
        {
            await SeedSchemaAsync(databasePath);
            await ExecuteAsync(databasePath, """
                INSERT INTO characters(character_id, account_id) VALUES(24, 14);
                INSERT INTO accounts(account_id) VALUES(14);
                """);
            var commandText = invalidCase switch
            {
                "duplicate-item-value" => """
                    INSERT INTO character_item_values(character_id, list_kind, sort_order, item_id, value)
                    VALUES(24, 'cooltime', 0, 1, 10), (24, 'cooltime', 0, 2, 20);
                    """,
                "duplicate-lock-id" => """
                    INSERT INTO character_item_locks(
                        character_id, equipment_lock_id, inventory_list_type, slot, state, remaining_seconds)
                    VALUES(24, 1, 3, 4, 1, 0), (24, 1, 3, 5, 1, 0);
                    """,
                "duplicate-lock-slot" => """
                    INSERT INTO character_item_locks(
                        character_id, equipment_lock_id, inventory_list_type, slot, state, remaining_seconds)
                    VALUES(24, 1, 3, 4, 1, 0), (24, 2, 3, 4, 1, 0);
                    """,
                _ => """
                    INSERT INTO character_item_locks(
                        character_id, equipment_lock_id, inventory_list_type, slot, state, remaining_seconds)
                    VALUES(24, 300, 3, 4, 1, -1);
                    """,
            };
            await ExecuteAsync(databasePath, commandText);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new InventorySide(databasePath).PerCall.LoadCharacterAsync(
                    new AccountId(14),
                    new CharacterId(24),
                    CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("duplicate")]
    [InlineData("wrong-owner")]
    public async Task LoadAsync_RejectsInvalidStoredCharacterItems(string invalidCase)
    {
        var databasePath = CreateDatabase();
        try
        {
            await SeedSchemaAsync(databasePath);
            await ExecuteAsync(databasePath,
                """
                INSERT INTO characters(character_id, account_id) VALUES(23, 13);
                INSERT INTO accounts(account_id) VALUES(13);
                """);
            var validCore = CreateCore(1, 3003, 31);
            var firstCore = invalidCase == "malformed" ? new byte[81] : validCore;
            var ownerScope = invalidCase == "wrong-owner" ? "account" : "character";
            var ownerId = invalidCase == "wrong-owner" ? 13 : 23;
            await ExecuteAsync(databasePath, """
                INSERT INTO character_new_items(
                    item_uid, owner_scope, owner_id, character_id, list_type, slot_index, item_core)
                VALUES(301, @ownerScope, @ownerId, 23, 0, 5, @core);
                """,
                ("@ownerScope", ownerScope),
                ("@ownerId", ownerId),
                ("@core", firstCore));
            if (invalidCase == "duplicate")
            {
                await ExecuteAsync(databasePath, """
                    INSERT INTO character_new_items(
                        item_uid, owner_scope, owner_id, character_id, list_type, slot_index, item_core)
                    VALUES(302, 'character', 23, 23, 0, 5, @core);
                    """, ("@core", validCore));
            }

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new InventorySide(databasePath).PerCall.LoadCharacterAsync(
                    new AccountId(13),
                    new CharacterId(23),
                    CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("wrong-container")]
    [InlineData("duplicate")]
    public async Task LoadAccountAsync_RejectsInvalidStoredAccountCargo(string invalidCase)
    {
        var databasePath = CreateDatabase();
        try
        {
            await SeedSchemaAsync(databasePath);
            await ExecuteAsync(databasePath, """
                INSERT INTO characters(character_id, account_id) VALUES(27, 17);
                INSERT INTO accounts(account_id) VALUES(17);
                """);
            var validCore = CreateCore(1, 4004, 41);
            var firstCore = invalidCase == "malformed" ? new byte[81] : validCore;
            var listType = invalidCase == "wrong-container" ? 3 : 12;
            await ExecuteAsync(databasePath, """
                INSERT INTO account_cargo_new_items(
                    item_uid, account_id, character_id, list_type, slot_index, item_core)
                VALUES(401, 17, 27, @listType, 5, @core);
                """,
                ("@listType", listType),
                ("@core", firstCore));
            if (invalidCase == "duplicate")
            {
                await ExecuteAsync(databasePath, """
                    INSERT INTO account_cargo_new_items(
                        item_uid, account_id, character_id, list_type, slot_index, item_core)
                    VALUES(402, 17, 27, 12, 5, @core);
                    """, ("@core", validCore));
            }

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new InventorySide(databasePath).PerCall.LoadAccountAsync(
                    new AccountId(17),
                    new CharacterId(27),
                    CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private static void AssertCore(InventoryItemCoreState core, int itemId, int value)
    {
        Assert.Equal(itemId, core.ItemId);
        Assert.Equal(value, core.Value);
        Assert.Equal((byte)0xA5, core.Attribute);
        Assert.Equal((ushort)0x1234, core.Durability);
        Assert.Equal(0x11223344, core.EnchantCardId);
        Assert.Equal(-1234567, core.Marker16);
        Assert.Equal(new ChronicleOptionState(0x10203040, 2, 3, 4, 5), core.ChronicleOption0);
        Assert.Equal(new ChronicleOptionState(0x20304050, 6, 7, 8, 9), core.ChronicleOption1);
        Assert.Equal(1700000000, core.ExpireTime);
        Assert.Equal(new RandomOptionState(11, 12, 13), core.RandomOption0);
        Assert.Equal(new RandomOptionState(20, 21, 22), core.RandomOptionChange);
        Assert.Equal((ushort)0x6677, core.TailUnknown0);
        Assert.Equal((byte)31, core.EquipmentLockId);
    }

    private static byte[] CreateCore(byte itemKind, int itemId, int value)
    {
        var core = new byte[82];
        core[0] = itemKind;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5), value);
        core[9] = 0xA5;
        BinaryPrimitives.WriteUInt16LittleEndian(core.AsSpan(10), 0x1234);
        core[12] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(13), 0x11223344);
        core[17] = 2;
        core[18] = 0x80;
        BinaryPrimitives.WriteUInt16LittleEndian(core.AsSpan(19), 0x3456);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(21), -1234567);
        WriteChronicle(core, 25, 0x10203040, 2, 3, 4, 5);
        WriteChronicle(core, 33, 0x20304050, 6, 7, 8, 9);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(41), 1700000000);
        core[45] = 2;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(46), 0x30405060);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(50), 0x40506070);
        BinaryPrimitives.WriteUInt16LittleEndian(core.AsSpan(54), 0x5566);
        WriteRandom(core, 56, 11, 12, 13);
        WriteRandom(core, 59, 14, 15, 16);
        WriteRandom(core, 62, 17, 18, 19);
        core[65] = 3;
        core[66] = 1;
        core[67] = 4;
        WriteRandom(core, 68, 20, 21, 22);
        core[71] = 23;
        core[72] = 24;
        core[73] = 25;
        BinaryPrimitives.WriteUInt16LittleEndian(core.AsSpan(74), 0x6677);
        core[76] = 26;
        core[77] = 27;
        core[78] = 28;
        core[79] = 29;
        core[80] = 30;
        core[81] = 31;
        return core;
    }

    private static void WriteChronicle(
        byte[] core, int offset, int id, byte job, byte growType, byte equipmentType, byte option)
    {
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(offset), id);
        core[offset + 4] = job;
        core[offset + 5] = growType;
        core[offset + 6] = equipmentType;
        core[offset + 7] = option;
    }

    private static void WriteRandom(byte[] core, int offset, byte type, byte value1, byte value2)
    {
        core[offset] = type;
        core[offset + 1] = value1;
        core[offset + 2] = value2;
    }

    private static string CreateDatabase() =>
        Path.Combine(Path.GetTempPath(), $"dfo-inventory-{Guid.NewGuid():N}.db");

    private static async Task SeedSchemaAsync(string databasePath) =>
        await ExecuteAsync(databasePath, """
            CREATE TABLE characters(character_id INTEGER PRIMARY KEY, account_id INTEGER NOT NULL);
            CREATE TABLE accounts(
                account_id INTEGER PRIMARY KEY,
                cube_black INTEGER NOT NULL DEFAULT 0,
                cube_white INTEGER NOT NULL DEFAULT 0,
                cube_red INTEGER NOT NULL DEFAULT 0,
                cube_blue INTEGER NOT NULL DEFAULT 0,
                cube_clear INTEGER NOT NULL DEFAULT 0,
                cube_gold INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE character_new_items(
                item_uid INTEGER PRIMARY KEY,
                owner_scope TEXT NOT NULL,
                owner_id INTEGER NOT NULL,
                character_id INTEGER,
                list_type INTEGER NOT NULL,
                slot_index INTEGER NOT NULL,
                item_core BLOB NOT NULL);
            CREATE TABLE account_cargo_new_items(
                item_uid INTEGER PRIMARY KEY,
                account_id INTEGER NOT NULL,
                character_id INTEGER,
                list_type INTEGER NOT NULL,
                slot_index INTEGER NOT NULL,
                item_core BLOB NOT NULL);
            CREATE TABLE character_container_state(
                character_id INTEGER NOT NULL,
                list_type INTEGER NOT NULL,
                list_param16 INTEGER NOT NULL);
            CREATE TABLE account_cargo_state(
                account_id INTEGER PRIMARY KEY,
                selection_key INTEGER NOT NULL,
                value32 INTEGER NOT NULL,
                item_count INTEGER NOT NULL);
            CREATE TABLE character_gold_limits(
                character_id INTEGER PRIMARY KEY,
                gold_carry_limit INTEGER NOT NULL,
                auction_gold_limit INTEGER NOT NULL);
            CREATE TABLE character_item_values(
                character_id INTEGER NOT NULL,
                list_kind TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                item_id INTEGER NOT NULL,
                value INTEGER NOT NULL);
            CREATE TABLE character_item_locks(
                character_id INTEGER NOT NULL,
                equipment_lock_id INTEGER NOT NULL,
                inventory_list_type INTEGER NOT NULL,
                slot INTEGER NOT NULL,
                state INTEGER NOT NULL,
                remaining_seconds INTEGER);
            CREATE TABLE character_rental_items(
                character_id INTEGER NOT NULL,
                shop_entry_id INTEGER NOT NULL,
                inventory_template_id INTEGER NOT NULL DEFAULT 0,
                expire_time INTEGER NOT NULL);
            CREATE TABLE rental_items(
                character_id INTEGER NOT NULL,
                item_id INTEGER NOT NULL,
                inventory_template_id INTEGER NOT NULL DEFAULT 0,
                expire_time INTEGER NOT NULL);
            CREATE TABLE character_items(
                item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
                owner_scope TEXT NOT NULL,
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
                extra_json TEXT NOT NULL DEFAULT '{}');
            CREATE TABLE account_cargo_items(
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
                extra_json TEXT NOT NULL DEFAULT '{}');
            """);

    private static async Task ExecuteAsync(
        string databasePath,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
