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
                INSERT INTO characters(character_id, account_id) VALUES(21, 11), (22, 11);
                INSERT INTO accounts(account_id, cube_red, soul_unique) VALUES(11, 99, 7);
                INSERT INTO items(item_uid, item_core) VALUES(101, @characterCore);
                INSERT INTO item_slots(
                    owner_scope, owner_id, list_type, slot_index, kind, id, core_uid, count)
                VALUES('character', 21, 3, 7, 'world', 101, 101, 1);
                INSERT INTO items(item_uid, cargo_character_id, item_core)
                VALUES(202, 21, @cargoCore);
                INSERT INTO item_slots(
                    owner_scope, owner_id, list_type, slot_index, kind, id, core_uid, count)
                VALUES('account', 11, 12, 4, 'world', 202, 202, 1);
                INSERT INTO character_container_state(character_id, list_type, list_param16)
                VALUES(21, 2, 48);
                INSERT INTO account_cargo_state(account_id, selection_key, value32, item_count)
                VALUES(11, 16, 3456, 1);
                INSERT INTO character_stats(character_id, gold_carry_limit, auction_gold_limit)
                VALUES(21, 400000000, 500000000);
                INSERT INTO character_item_cooldowns(character_id, sort_order, item_id, value)
                VALUES(21, 2, 7002, 72),
                      (21, 0, 7000, 70);
                INSERT INTO character_item_effects(character_id, sort_order, item_id, value)
                VALUES(21, 1, 8001, 81);
                INSERT INTO character_item_locks(
                    character_id, equipment_lock_id, inventory_list_type, slot, state, remaining_seconds)
                VALUES(21, 9, 3, 7, 1, 0),
                      (21, 10, 12, 4, 2, 3600);
                INSERT INTO character_rentals(
                    character_id, item_id, inventory_template_id, expire_time)
                VALUES(21, 9001, 1001, 1900000000),
                      (21, 9002, 1002, 1900000100);
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
            var secondCharacterAccountResult = await repository.LoadAccountAsync(
                new AccountId(11),
                new CharacterId(22),
                CancellationToken.None);

            var characterItem = Assert.Single(characterResult.CharacterItems);
            Assert.Equal(ContainerId.ForCharacter(new CharacterId(21), InventoryContainerType.Equipment),
                characterItem.ContainerId);
            Assert.Equal(7, characterItem.SlotIndex);
            AssertCore(characterItem.Snapshot.ToCore(), 1001, 17);

            var cargoItem = Assert.Single(accountResult.AccountCargoItems);
            Assert.Equal(ContainerId.ForAccount(new AccountId(11), InventoryContainerType.AccountCargo),
                cargoItem.ContainerId);
            Assert.Equal(4, cargoItem.SlotIndex);
            AssertCore(cargoItem.Snapshot.ToCore(), 2002, 23);
            Assert.Equal((ushort)48, Assert.Single(characterResult.CharacterContainerParameters).Value);
            Assert.Equal((ushort)16, accountResult.AccountCargoState.SelectionKey);
            Assert.Equal(3456, accountResult.AccountCargoState.Value32);
            Assert.Equal((ushort)1, accountResult.AccountCargoState.StoredItemCount);
            Assert.Equal(
                [
                    new InventoryRuntimeValueState(356, 3035, 99),
                    new InventoryRuntimeValueState(363, 10099774, 7),
                ],
                accountResult.AccountVirtualResources);
            Assert.Equal(
                accountResult.AccountVirtualResources,
                secondCharacterAccountResult.AccountVirtualResources);
            // Slot 0 is the gold this character carries, and it is answered for every character
            // this server has rather than only for one that has been given some. Zero is a figure
            // a character can carry; "no entry" was how the old reading said "no item row here",
            // and gold stopped being an item row.
            Assert.Equal(
                new InventoryRuntimeValueState(0, 0, 0),
                Assert.Single(characterResult.CharacterVirtualResources));
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
                    INSERT INTO character_rentals(
                        character_id, item_id, inventory_template_id, expire_time)
                    VALUES(25, 1, 101, 1000), (25, 1, 102, 1001);
                    """,
                "duplicate-template" => """
                    INSERT INTO character_rentals(
                        character_id, item_id, inventory_template_id, expire_time)
                    VALUES(25, 1, 101, 1000), (25, 2, 101, 1001);
                    """,
                "invalid-id" => """
                    INSERT INTO character_rentals(
                        character_id, item_id, inventory_template_id, expire_time)
                    VALUES(25, 0, 101, 1000);
                    """,
                _ => """
                    INSERT INTO character_rentals(
                        character_id, item_id, inventory_template_id, expire_time)
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
                    INSERT INTO character_item_cooldowns(character_id, sort_order, item_id, value)
                    VALUES(24, 0, 1, 10), (24, 0, 2, 20);
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
            await ExecuteAsync(databasePath, """
                INSERT INTO items(item_uid, item_core) VALUES(301, @core);
                INSERT INTO item_slots(
                    owner_scope, owner_id, list_type, slot_index, kind, id, core_uid, count)
                VALUES('character', 23, 0, 5, 'world', 301, 301, 1);
                """,
                ("@core", firstCore));
            if (invalidCase == "duplicate")
            {
                await ExecuteAsync(databasePath, """
                    INSERT INTO items(item_uid, item_core) VALUES(302, @core);
                    INSERT INTO item_slots(
                        owner_scope, owner_id, list_type, slot_index, kind, id, core_uid, count)
                    VALUES('character', 23, 0, 5, 'world', 302, 302, 1);
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

    /// <summary>
    /// Another owner's item is not this character's, and is not read as though it were.
    /// </summary>
    /// <remarks>
    /// This replaces a case that asked what happens when a row the load reads names someone else as
    /// its owner. One table holds every owner's items now and the read names the owner it wants, so
    /// such a row is not read at all and there is nothing left to refuse. What matters instead is
    /// that the read really does leave it out -- a filter that lost its owner would hand one
    /// character another's inventory, and no refusal further on would notice.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_LeavesOutTheItemsOfEveryOwnerButTheOneAskedFor()
    {
        var databasePath = CreateDatabase();
        try
        {
            await SeedSchemaAsync(databasePath);
            await ExecuteAsync(databasePath, """
                INSERT INTO characters(character_id, account_id) VALUES(23, 13);
                INSERT INTO characters(character_id, account_id) VALUES(28, 13);
                INSERT INTO accounts(account_id) VALUES(13);
                """);
            await ExecuteAsync(databasePath, """
                INSERT INTO items(item_uid, item_core)
                VALUES(301, @mine), (302, @theirs), (303, @cargo);
                INSERT INTO item_slots(
                    owner_scope, owner_id, list_type, slot_index, kind, id, core_uid, count)
                VALUES('character', 23, 0, 5, 'world', 301, 301, 1),
                      ('character', 28, 0, 5, 'world', 302, 302, 1),
                      ('account', 13, 12, 5, 'world', 303, 303, 1);
                """,
                ("@mine", CreateCore(1, 3003, 31)),
                ("@theirs", CreateCore(1, 4004, 41)),
                ("@cargo", CreateCore(1, 5005, 51)));

            var loaded = await new InventorySide(databasePath).PerCall.LoadCharacterAsync(
                new AccountId(13), new CharacterId(23), CancellationToken.None);

            var item = Assert.Single(loaded.CharacterItems);

            // Historical world slots retain their persisted identity, independent of fixture PVF rules.
            Assert.Equal(new ItemIdentity(301), item.ItemUid);
            Assert.Equal(3003, item.Snapshot.ToCore().ItemId);
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
    public async Task LoadAccountAsync_ValidatesOnlyTheAccountCargoContainer(string invalidCase)
    {
        var databasePath = CreateDatabase();
        try
        {
            await SeedSchemaAsync(databasePath);
            await ExecuteAsync(databasePath, """
                INSERT INTO characters(character_id, account_id) VALUES(27, 17);
                INSERT INTO accounts(account_id) VALUES(17);
                """);
            var firstCore = invalidCase == "malformed" ? new byte[81] : CreateCore(1, 4004, 41);
            var listType = invalidCase == "wrong-container" ? 3 : 12;
            await ExecuteAsync(databasePath, """
                INSERT INTO items(item_uid, cargo_character_id, item_core)
                VALUES(401, 27, @core);
                INSERT INTO item_slots(
                    owner_scope, owner_id, list_type, slot_index, kind, id, core_uid, count)
                VALUES('account', 17, @listType, 5, 'world', 401, 401, 1);
                """,
                ("@listType", listType),
                ("@core", firstCore));

            var repository = new InventorySide(databasePath).PerCall;
            if (invalidCase == "malformed")
                await Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await repository.LoadAccountAsync(new AccountId(17), new CharacterId(27), CancellationToken.None));
            else
                Assert.Empty((await repository.LoadAccountAsync(new AccountId(17), new CharacterId(27),
                    CancellationToken.None)).AccountCargoItems);
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

    /// <summary>
    /// The tables these tests read, declared here so a row can be written that the real shape
    /// forbids.
    /// </summary>
    /// <remarks>
    /// The positions are deliberately not held unique here. Two things in one position is exactly
    /// what the duplicate case below has to put into the database in order to find out what the
    /// load does with it, and against the real declaration it could not be written at all -- which
    /// would leave the load's own refusal untested rather than tested.
    /// </remarks>
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
                cube_gold INTEGER NOT NULL DEFAULT 0,
                soul_common INTEGER NOT NULL DEFAULT 0,
                soul_advanced INTEGER NOT NULL DEFAULT 0,
                soul_rare INTEGER NOT NULL DEFAULT 0,
                soul_unique INTEGER NOT NULL DEFAULT 0,
                soul_legendary INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE items(
                item_uid INTEGER PRIMARY KEY,
                item_core BLOB NOT NULL,
                cargo_character_id INTEGER,
                avatar_expire_date INTEGER,
                avatar_clear_id INTEGER,
                avatar_jewel_socket BLOB,
                avatar_color1 INTEGER,
                avatar_color2 INTEGER,
                avatar_delete_date INTEGER,
                creature_field04 INTEGER, creature_mode_flag INTEGER, creature_progress_value INTEGER,
                creature_mode1_field0a INTEGER, creature_mode1_field0b INTEGER,
                creature_field_after_value INTEGER, creature_text BLOB, creature_tail_flag INTEGER, appearance_item_id INTEGER);
            CREATE TABLE item_slots(
                owner_scope TEXT NOT NULL,
                owner_id INTEGER NOT NULL,
                list_type INTEGER NOT NULL,
                slot_index INTEGER NOT NULL,
                kind TEXT NOT NULL,
                id INTEGER NOT NULL,
                core_uid INTEGER NOT NULL,
                count INTEGER NOT NULL);
            CREATE TABLE character_container_state(
                character_id INTEGER NOT NULL,
                list_type INTEGER NOT NULL,
                list_param16 INTEGER NOT NULL);
            CREATE TABLE account_cargo_state(
                account_id INTEGER PRIMARY KEY,
                selection_key INTEGER NOT NULL,
                value32 INTEGER NOT NULL,
                item_count INTEGER NOT NULL);
            CREATE TABLE character_stats(
                character_id INTEGER PRIMARY KEY,
                gold_carry_limit INTEGER NOT NULL,
                auction_gold_limit INTEGER NOT NULL);
            CREATE TABLE character_item_cooldowns(
                character_id INTEGER NOT NULL,
                sort_order INTEGER NOT NULL,
                item_id INTEGER NOT NULL,
                value INTEGER NOT NULL);
            CREATE TABLE character_item_effects(
                character_id INTEGER NOT NULL,
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
            CREATE TABLE character_rentals(
                character_id INTEGER NOT NULL,
                item_id INTEGER NOT NULL,
                inventory_template_id INTEGER NOT NULL DEFAULT 0,
                expire_time INTEGER NOT NULL);
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
