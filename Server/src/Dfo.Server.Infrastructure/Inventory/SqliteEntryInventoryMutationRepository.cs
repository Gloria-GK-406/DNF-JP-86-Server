using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Inventory;

internal sealed class SqliteEntryInventoryMutationRepository : IEntryInventoryMutationRepository
{
    private const string CounterPrefix = "pvf_daily_refill_item_";

    /// <summary>Where a swapped row waits while the slot it came from is refilled.</summary>
    private const int ParkedSlotIndex = -1;
    private readonly string connectionString;

    public SqliteEntryInventoryMutationRepository(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS character_daily_counters(
                character_id INTEGER, counter_key TEXT, period TEXT, value INTEGER,
                PRIMARY KEY(character_id,counter_key));
            """;
        command.ExecuteNonQuery();
    }

    public async ValueTask<DailyRefillMutationResult> ApplyDailyRefillAsync(
        AccountId accountId,
        CharacterId characterId,
        IReadOnlyList<DailyRefillRule> rules,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await VerifyOwnerAsync(connection, transaction, accountId, characterId, cancellationToken).ConfigureAwait(false);
        var period = preparedAt.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd");
        var committed = new List<DailyRefillCommittedGrant>();

        foreach (var rule in rules)
        {
            var key = CounterPrefix + rule.ItemId;
            if (await IsClaimedAsync(connection, transaction, characterId, key, period, cancellationToken)
                    .ConfigureAwait(false))
                continue;

            var items = await LoadMatchingAsync(connection, transaction, characterId, rule.ItemId, cancellationToken)
                .ConfigureAwait(false);
            var current = items.Sum(value => Math.Max(0, ReadCount(value.Core)));
            var grant = PvfEntryLifecycleContentSource.CalculateGrant(rule, current);
            if (grant > 0)
            {
                if (items.Count == 0)
                {
                    var slot = await NextMainSlotAsync(
                            connection, transaction, characterId, rule.SlotStart, rule.SlotEnd,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (rule.ItemKind == 0)
                        throw new InvalidDataException($"Daily refill item {rule.ItemId} has no authoritative PVF item kind.");
                    var core = CreateStackableCore(rule.ItemId, grant, rule.ItemKind);
                    await InsertAsync(connection, transaction, characterId, slot, core, cancellationToken)
                        .ConfigureAwait(false);
                    committed.Add(new DailyRefillCommittedGrant(
                        InventoryContainerType.Main, slot, rule.ItemId, grant, grant));
                }
                else
                {
                    var item = items[0];
                    var count = checked(ReadCount(item.Core) + grant);
                    WriteCount(item.Core, count);
                    await UpdateAsync(connection, transaction, item.Uid, item.Core, cancellationToken)
                        .ConfigureAwait(false);
                    committed.Add(new DailyRefillCommittedGrant(
                        InventoryContainerType.Main, item.Slot, rule.ItemId, grant, count));
                }
            }
            await ClaimAsync(connection, transaction, characterId, key, period, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new DailyRefillMutationResult(committed
            .OrderBy(value => (byte)value.ContainerType).ThenBy(value => value.SlotIndex).ToArray());
    }

    private static async ValueTask VerifyOwnerAsync(SqliteConnection connection, SqliteTransaction transaction,
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM characters WHERE character_id=@cid AND account_id=@aid LIMIT 1";
        command.Parameters.AddWithValue("@cid", characterId.Value); command.Parameters.AddWithValue("@aid", accountId.Value);
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
            throw new InvalidDataException("Daily refill character ownership does not match the account.");
    }

    private static async ValueTask<bool> IsClaimedAsync(SqliteConnection connection, SqliteTransaction transaction,
        CharacterId characterId, string key, string period, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT value FROM character_daily_counters WHERE character_id=@cid AND counter_key=@key AND period=@period";
        command.Parameters.AddWithValue("@cid", characterId.Value); command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@period", period);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0) > 0;
    }

    private static async ValueTask ClaimAsync(SqliteConnection connection, SqliteTransaction transaction,
        CharacterId characterId, string key, string period, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO character_daily_counters(character_id,counter_key,period,value) VALUES(@cid,@key,@period,1)
            ON CONFLICT(character_id,counter_key) DO UPDATE SET period=excluded.period,value=1
            """;
        command.Parameters.AddWithValue("@cid", characterId.Value); command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@period", period);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("Daily refill claim was not persisted.");
    }

    /// <summary>
    /// Places one item in the character's main inventory.
    /// </summary>
    /// <remarks>
    /// Stacked onto an existing pile when the item's own stack limit allows it and a pile has
    /// room, otherwise placed in the first free slot of its band. That order is the daily refill's
    /// and is kept deliberately: a player who receives a second potion expects the count on the
    /// first to go up, not a second entry beside it.
    ///
    /// One transaction covers the search and the write, so two grants arriving together cannot
    /// both decide the same slot is free.
    /// </remarks>
    public async ValueTask<GrantedInventoryItem?> GrantItemAsync(
        CharacterId characterId,
        int itemId,
        int count,
        ItemPlacement placement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "A granted count must be positive.");

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (placement.StackLimit > 1)
        {
            var existing = await LoadMatchingAsync(
                connection, transaction, characterId, itemId, cancellationToken).ConfigureAwait(false);
            foreach (var row in existing)
            {
                var current = ReadCount(row.Core);
                if (current >= placement.StackLimit)
                    continue;
                var added = Math.Min(count, placement.StackLimit - current);
                WriteCount(row.Core, current + added);
                await UpdateAsync(connection, transaction, row.Uid, row.Core, cancellationToken)
                    .ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new GrantedInventoryItem(row.Slot, itemId, added);
            }
        }

        int slot;
        try
        {
            slot = await NextMainSlotAsync(
                connection, transaction, characterId,
                placement.SlotStart, placement.SlotEnd, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            // The band is full. Nothing is written and the caller is told so.
            return null;
        }

        var placed = placement.StackLimit > 0 ? Math.Min(count, placement.StackLimit) : count;
        await InsertAsync(
            connection, transaction, characterId, slot,
            CreateStackableCore(itemId, placed, placement.ItemKind), cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new GrantedInventoryItem(slot, itemId, placed);
    }

    private static async ValueTask<List<ItemRow>> LoadMatchingAsync(SqliteConnection connection,
        SqliteTransaction transaction, CharacterId characterId, int itemId, CancellationToken cancellationToken)
    {
        var rows = new List<ItemRow>();
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT item_uid,slot_index,item_core FROM character_new_items WHERE character_id=@cid AND list_type=0 ORDER BY slot_index";
        command.Parameters.AddWithValue("@cid", characterId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var core = (byte[])reader.GetValue(2);
            if (core.Length != 82) throw new InvalidDataException("Daily refill item core is malformed.");
            if (BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1, 4)) == itemId)
                rows.Add(new ItemRow(reader.GetInt64(0), reader.GetInt32(1), core));
        }
        return rows;
    }


    /// <summary>Reads every occupied slot of the main inventory, in the order the client draws them.</summary>
    public async ValueTask<IReadOnlyList<InventoryItemState>> ReadMainItemsAsync(
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT item_uid,slot_index,item_core FROM character_new_items WHERE character_id=@cid AND list_type=0 ORDER BY slot_index,item_uid";
        command.Parameters.AddWithValue("@cid", characterId.Value);
        var items = new List<InventoryItemState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var storageEntryId = reader.GetInt64(0);
            var core = (byte[])reader.GetValue(2);
            if (core.Length != 82)
                throw new InvalidDataException("Inventory item core is malformed.");
            items.Add(new InventoryItemState(
                storageEntryId, reader.GetInt32(1), SqliteInventoryEntryRepository.DecodeItemCore(core)));
        }

        return items;
    }

    /// <summary>Reads what one slot of the main inventory holds.</summary>
    public async ValueTask<MainInventoryOccupant?> ReadMainSlotAsync(
        CharacterId characterId,
        int slotIndex,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadMainSlotAsync(connection, null, characterId, slotIndex, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Takes items out of one pile, deleting the row when the last of it goes.</summary>
    public async ValueTask<ConsumedInventoryItem?> ConsumeMainItemAsync(
        CharacterId characterId,
        long storageEntryId,
        int count,
        CancellationToken cancellationToken)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "A consumed count must be positive.");

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var item = await ReadMainEntryAsync(
            connection, transaction, characterId, storageEntryId, cancellationToken).ConfigureAwait(false);
        if (item is null || count > item.Count)
            return null;

        var remaining = item.Count - count;
        if (remaining == 0)
            await DeleteEntryAsync(connection, transaction, storageEntryId, cancellationToken)
                .ConfigureAwait(false);
        else
            await WriteEntryCountAsync(
                connection, transaction, storageEntryId, remaining, cancellationToken)
                .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ConsumedInventoryItem(item.SlotIndex, item.ItemId, remaining);
    }

    /// <summary>Reads what two slots of the main inventory hold.</summary>
    public async ValueTask<MainInventorySlotPair> ReadMainSlotsAsync(
        CharacterId characterId,
        int sourceSlotIndex,
        int destinationSlotIndex,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return new MainInventorySlotPair(
            await ReadMainSlotAsync(connection, null, characterId, sourceSlotIndex, cancellationToken)
                .ConfigureAwait(false),
            await ReadMainSlotAsync(connection, null, characterId, destinationSlotIndex, cancellationToken)
                .ConfigureAwait(false));
    }

    /// <summary>
    /// Applies a decided move as one transaction.
    /// </summary>
    /// <remarks>
    /// A swap cannot write both rows straight across: the slot is part of a unique key, so the
    /// first update would collide with the row it is about to displace. The moving row is parked
    /// on a negative slot for the length of the transaction instead -- a value no real slot takes,
    /// and one no reader ever sees because it never outlives the transaction that wrote it.
    /// </remarks>
    public async ValueTask<bool> ApplyMainMoveAsync(
        CharacterId characterId,
        MainInventoryMovePlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var moving = await ReadMainEntryAsync(
            connection, transaction, characterId, plan.MovingEntryId, cancellationToken).ConfigureAwait(false);
        if (moving is null || plan.MoveCount <= 0 || plan.MoveCount > moving.Count)
            return false;

        var displaced = await ReadMainSlotAsync(
            connection, transaction, characterId, plan.TargetSlotIndex, cancellationToken).ConfigureAwait(false);
        if (displaced?.StorageEntryId != plan.DisplacedEntryId)
            return false;

        switch (plan.Kind)
        {
            case MainInventoryMoveKind.Relocate when displaced is null && plan.MoveCount == moving.Count:
                await MoveSlotAsync(
                    connection, transaction, moving.StorageEntryId, plan.TargetSlotIndex, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case MainInventoryMoveKind.Split when displaced is null && plan.MoveCount < moving.Count:
                await WriteEntryCountAsync(
                    connection, transaction, moving.StorageEntryId, moving.Count - plan.MoveCount,
                    cancellationToken).ConfigureAwait(false);
                await InsertAsync(
                    connection, transaction, characterId, plan.TargetSlotIndex,
                    CreateStackableCore(moving.ItemId, plan.MoveCount, moving.ItemKind), cancellationToken)
                    .ConfigureAwait(false);
                break;

            case MainInventoryMoveKind.Swap when displaced is not null && plan.MoveCount == moving.Count:
                await MoveSlotAsync(
                    connection, transaction, moving.StorageEntryId, ParkedSlotIndex, cancellationToken)
                    .ConfigureAwait(false);
                await MoveSlotAsync(
                    connection, transaction, displaced.StorageEntryId, moving.SlotIndex, cancellationToken)
                    .ConfigureAwait(false);
                await MoveSlotAsync(
                    connection, transaction, moving.StorageEntryId, plan.TargetSlotIndex, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case MainInventoryMoveKind.Merge when displaced is not null && displaced.ItemId == moving.ItemId:
                await WriteEntryCountAsync(
                    connection, transaction, displaced.StorageEntryId,
                    checked(displaced.Count + plan.MoveCount), cancellationToken).ConfigureAwait(false);
                if (plan.MoveCount == moving.Count)
                    await DeleteEntryAsync(connection, transaction, moving.StorageEntryId, cancellationToken)
                        .ConfigureAwait(false);
                else
                    await WriteEntryCountAsync(
                        connection, transaction, moving.StorageEntryId, moving.Count - plan.MoveCount,
                        cancellationToken).ConfigureAwait(false);
                break;

            default:
                return false;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async ValueTask<MainInventoryOccupant?> ReadMainSlotAsync(
        SqliteConnection connection, SqliteTransaction? transaction, CharacterId characterId,
        int slotIndex, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT item_uid,slot_index,item_core FROM character_new_items WHERE character_id=@cid AND list_type=0 AND slot_index=@slot";
        command.Parameters.AddWithValue("@cid", characterId.Value);
        command.Parameters.AddWithValue("@slot", slotIndex);
        return await ReadOccupantAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<MainInventoryOccupant?> ReadMainEntryAsync(
        SqliteConnection connection, SqliteTransaction transaction, CharacterId characterId,
        long storageEntryId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT item_uid,slot_index,item_core FROM character_new_items WHERE character_id=@cid AND list_type=0 AND item_uid=@uid";
        command.Parameters.AddWithValue("@cid", characterId.Value);
        command.Parameters.AddWithValue("@uid", storageEntryId);
        return await ReadOccupantAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<MainInventoryOccupant?> ReadOccupantAsync(
        SqliteCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        var core = (byte[])reader.GetValue(2);
        if (core.Length != 82)
            throw new InvalidDataException("Inventory item core is malformed.");
        return new MainInventoryOccupant(
            reader.GetInt64(0),
            reader.GetInt32(1),
            core[0],
            BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1, 4)),
            ReadCount(core));
    }

    private static async ValueTask MoveSlotAsync(SqliteConnection connection, SqliteTransaction transaction,
        long uid, int slotIndex, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE character_new_items SET slot_index=@slot,updated_at=CURRENT_TIMESTAMP WHERE item_uid=@uid";
        command.Parameters.AddWithValue("@slot", slotIndex); command.Parameters.AddWithValue("@uid", uid);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("Inventory slot update failed.");
    }

    private static async ValueTask WriteEntryCountAsync(SqliteConnection connection,
        SqliteTransaction transaction, long uid, int count, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT item_core FROM character_new_items WHERE item_uid=@uid";
        command.Parameters.AddWithValue("@uid", uid);
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not byte[] core)
            throw new InvalidDataException("Inventory item disappeared mid-move.");
        WriteCount(core, count);
        await UpdateAsync(connection, transaction, uid, core, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask DeleteEntryAsync(SqliteConnection connection,
        SqliteTransaction transaction, long uid, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM character_new_items WHERE item_uid=@uid";
        command.Parameters.AddWithValue("@uid", uid);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("Inventory item delete failed.");
    }

    private static async ValueTask<int> NextMainSlotAsync(SqliteConnection connection, SqliteTransaction transaction,
        CharacterId characterId, int slotStart, int slotEnd, CancellationToken cancellationToken)
    {
        if (slotStart < 0 || slotEnd < slotStart || slotEnd > ushort.MaxValue)
            throw new InvalidDataException($"Daily refill slot range {slotStart}-{slotEnd} is invalid.");
        var occupied = new HashSet<int>();
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT slot_index FROM character_new_items WHERE character_id=@cid AND list_type=0";
        command.Parameters.AddWithValue("@cid", characterId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) occupied.Add(reader.GetInt32(0));
        for (var slot = slotStart; slot <= slotEnd; slot++) if (!occupied.Contains(slot)) return slot;
        throw new InvalidDataException($"Daily refill inventory range {slotStart}-{slotEnd} is full.");
    }

    private static async ValueTask InsertAsync(SqliteConnection connection, SqliteTransaction transaction,
        CharacterId characterId, int slot, byte[] core, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core) VALUES('character',@cid,@cid,0,@slot,@core)";
        command.Parameters.AddWithValue("@cid", characterId.Value); command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@core", core);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("Daily refill item insert failed.");
    }

    private static async ValueTask UpdateAsync(SqliteConnection connection, SqliteTransaction transaction,
        long uid, byte[] core, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE character_new_items SET item_core=@core,updated_at=CURRENT_TIMESTAMP WHERE item_uid=@uid";
        command.Parameters.AddWithValue("@core", core); command.Parameters.AddWithValue("@uid", uid);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("Daily refill item update failed.");
    }

    private static byte[] CreateStackableCore(int itemId, int count, byte itemKind)
    {
        var core = new byte[82]; core[0] = itemKind;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), itemId);
        WriteCount(core, count);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(21, 4), -1);
        core[66] = byte.MaxValue;
        return core;
    }
    private static int ReadCount(byte[] core) => BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5, 4));
    private static void WriteCount(byte[] core, int count) => BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5, 4), count);
    private sealed record ItemRow(long Uid, int Slot, byte[] Core);
}
