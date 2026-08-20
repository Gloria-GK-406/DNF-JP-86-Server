using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Schema;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Inventory;

internal sealed class SqliteEntryInventoryMutationRepository : IEntryInventoryMutationRepository
{
    private const string CounterPrefix = "pvf_daily_refill_item_";

    private readonly string connectionString;

    private readonly IDailyRefillRuleService refillRules;

    private readonly IItemArrivalRuleService arrivalRules;

    public SqliteEntryInventoryMutationRepository(
        string databasePath,
        IDailyRefillRuleService refillRules,
        IItemArrivalRuleService arrivalRules)
    {
        this.refillRules = refillRules;
        this.arrivalRules = arrivalRules;
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        CharacterDatabaseSchema.Ensure(connectionString);
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
        var period = refillRules.PeriodOf(preparedAt);
        var committed = new List<DailyRefillCommittedGrant>();

        foreach (var rule in rules)
        {
            var key = CounterPrefix + rule.ItemId;
            var claimed = await ClaimedPeriodAsync(
                connection, transaction, characterId, key, cancellationToken).ConfigureAwait(false);
            if (!refillRules.IsDue(claimed, period))
                continue;

            var items = await LoadMatchingAsync(connection, transaction, characterId, rule.ItemId, cancellationToken)
                .ConfigureAwait(false);
            var current = items.Sum(value => Math.Max(0, ReadCount(value.Core)));
            var grant = refillRules.GrantFor(rule, current);
            if (grant > 0)
            {
                var occupied = await OccupiedSlotsAsync(
                    connection, transaction, characterId, cancellationToken).ConfigureAwait(false);
                var arrival = arrivalRules.ForDailyRefill(
                    [.. items.Select(value => new ItemPile(value.Slot, ReadCount(value.Core)))],
                    grant, rule.SlotStart, rule.SlotEnd, occupied);
                switch (arrival)
                {
                    case ItemArrival.IntoEmptySlot opened:
                        if (rule.ItemKind == 0)
                            throw new InvalidDataException($"Daily refill item {rule.ItemId} has no authoritative PVF item kind.");
                        await InsertAsync(
                            connection, transaction, characterId, opened.SlotIndex,
                            CreateStackableCore(rule.ItemId, opened.Count, rule.ItemKind),
                            cancellationToken).ConfigureAwait(false);
                        committed.Add(new DailyRefillCommittedGrant(
                            InventoryContainerType.Main, opened.SlotIndex, rule.ItemId,
                            opened.Count, opened.Count));
                        break;

                    case ItemArrival.OntoExistingPile topped:
                        var row = items.Single(value => value.Slot == topped.SlotIndex);
                        WriteCount(row.Core, topped.Total);
                        await UpdateAsync(connection, transaction, row.Uid, row.Core, cancellationToken)
                            .ConfigureAwait(false);
                        committed.Add(new DailyRefillCommittedGrant(
                            InventoryContainerType.Main, topped.SlotIndex, rule.ItemId,
                            topped.Added, topped.Total));
                        break;

                    default:
                        // The band is full. An entry that quietly skipped the refill would leave
                        // the player short with nothing said, so the whole entry fails instead.
                        throw new InvalidDataException(
                            $"Daily refill inventory range {rule.SlotStart}-{rule.SlotEnd} is full.");
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

    /// <summary>Which day is on record for this counter, or null when none ever was.</summary>
    /// <remarks>
    /// It reads the day and answers with it. Whether that day still counts is not a question about
    /// rows, and the row reader is not the place it gets decided.
    /// </remarks>
    private static async ValueTask<string?> ClaimedPeriodAsync(SqliteConnection connection,
        SqliteTransaction transaction, CharacterId characterId, string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT period FROM character_daily_counters WHERE character_id=@cid AND counter_key=@key AND value>0";
        command.Parameters.AddWithValue("@cid", characterId.Value); command.Parameters.AddWithValue("@key", key);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    /// <summary>Every slot of the main list that holds something.</summary>
    private static async ValueTask<IReadOnlySet<int>> OccupiedSlotsAsync(SqliteConnection connection,
        SqliteTransaction transaction, CharacterId characterId, CancellationToken cancellationToken)
    {
        var occupied = new HashSet<int>();
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT slot_index FROM character_new_items WHERE character_id=@cid AND list_type=0";
        command.Parameters.AddWithValue("@cid", characterId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            occupied.Add(reader.GetInt32(0));
        return occupied;
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
