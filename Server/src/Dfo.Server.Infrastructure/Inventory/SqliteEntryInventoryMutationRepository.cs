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
