using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

internal sealed partial class SqliteItemDomainStoreSession
{
    private sealed record CustodyItem(string Kind, long Uid, int ItemId, int Count, byte[]? Core);

    // Only these two private storage adapters supply identifiers. No caller supplies SQL or table names.
    private abstract class CustodyStorage(SqliteDatabaseParticipant participant)
    {
        protected abstract string Table { get; }
        protected abstract string Key { get; }
        protected abstract string InsertColumns { get; }
        protected abstract string InsertValues { get; }
        protected abstract void Bind(SqliteCommand command);

        internal async ValueTask<CustodyItem> ReadAsync(CancellationToken ct)
        {
            await using var command = await participant.CommandAsync(ct).ConfigureAwait(false);
            command.CommandText = $"SELECT kind,item_uid,item_id,item_count,item_core FROM {Table} WHERE {Key}";
            Bind(command);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("The custody location holds no item.");
            return ReadCustodyItem(reader, 0);
        }

        internal async ValueTask InsertAsync(CustodyItem item, CancellationToken ct)
        {
            await using var command = await participant.CommandAsync(ct).ConfigureAwait(false);
            command.CommandText = $"""
                INSERT INTO {Table}({InsertColumns},kind,item_uid,item_id,item_count,item_core)
                VALUES({InsertValues},@kind,@uid,@item,@count,@core)
                """;
            Bind(command);
            command.Parameters.AddWithValue("@kind", item.Kind);
            command.Parameters.AddWithValue("@uid", item.Uid);
            command.Parameters.AddWithValue("@item", item.ItemId);
            command.Parameters.AddWithValue("@count", item.Count);
            command.Parameters.AddWithValue("@core", (object?)item.Core ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        internal async ValueTask TakeAsync(int held, int amount, CancellationToken ct)
        {
            await using var command = await participant.CommandAsync(ct).ConfigureAwait(false);
            command.CommandText = amount == held
                ? $"DELETE FROM {Table} WHERE {Key} AND item_count=@held"
                : $"UPDATE {Table} SET item_count=@remaining WHERE {Key} AND item_count=@held";
            Bind(command);
            command.Parameters.AddWithValue("@held", held);
            command.Parameters.AddWithValue("@remaining", held - amount);
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Custody changed during a take.");
        }
    }

    private async ValueTask AcceptCustodyAsync(CustodyStorage storage, ItemRecord record, CancellationToken ct)
    {
        Active();
        ArgumentNullException.ThrowIfNull(record);
        var foreign = scope.IsForeign(record, identity);
        var uid = foreign ? 0 : Detached(record);
        var snapshot = record.Snapshot;
        Validate(snapshot);
        // Storage representation, not trading eligibility: these adapters carry no instance-detail columns.
        if (snapshot.AvatarDetail is not null || snapshot.CreatureDetail is not null || snapshot.IsMirror)
            throw new InvalidOperationException("Custody holds only items without instance details.");
        if (!snapshot.IsPile && (foreign || snapshot.ItemUid?.Value != uid))
            throw new InvalidOperationException("Equipment enters custody only as the detached record of its own row.");
        if (!foreign && await HasPositionAsync(uid, ct).ConfigureAwait(false))
            throw new InvalidOperationException("A detached item still occupies an inventory position.");
        await Mutate(async () =>
        {
            await storage.InsertAsync(new(snapshot.IsPile ? "stackable" : "equipment",
                snapshot.IsPile ? 0 : uid, snapshot.ItemId, snapshot.Count,
                snapshot.IsPile ? InventoryItemCore.Encode(snapshot.ToCore()) : null), ct).ConfigureAwait(false);
            if (snapshot.IsPile && uid != 0) await RemoveRowAsync(uid, ct).ConfigureAwait(false);
            await scope.AcceptAsync(record, identity, ct).ConfigureAwait(false);
            detached.Remove(record);
            return true;
        }).ConfigureAwait(false);
    }

    private async ValueTask<ItemRecord> TakeCustodyAsync(CustodyStorage storage, int? count, CancellationToken ct)
    {
        Active();
        var item = await storage.ReadAsync(ct).ConfigureAwait(false);
        var amount = count ?? item.Count;
        if (amount <= 0 || amount > item.Count || item.Kind == "equipment" && amount != 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        var snapshot = await CustodySnapshotAsync(item, ct).ConfigureAwait(false);
        return await Mutate(async () =>
        {
            await storage.TakeAsync(item.Count, amount, ct).ConfigureAwait(false);
            var piece = snapshot with { Count = amount };
            return Issue(item.Kind == "equipment" ? item.Uid : await InsertAsync(piece, ct).ConfigureAwait(false), piece);
        }).ConfigureAwait(false);
    }

    private async ValueTask<ItemSnapshot> CustodySnapshotAsync(CustodyItem item, CancellationToken ct)
    {
        if (item.Kind == "equipment")
        {
            if (await HasPositionAsync(item.Uid, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Equipment in custody also occupies a position.");
            var snapshot = await ReadMailWorldAsync(checked((int)item.Uid), ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Equipment in custody has no item row.");
            if (snapshot.ItemId != item.ItemId || snapshot.IsPile)
                throw new InvalidDataException("Custody disagrees with its equipment row.");
            return snapshot;
        }
        if (item.Kind != "stackable" || item.Core is not { Length: InventoryItemCore.Size })
            throw new InvalidDataException("Pile custody has no supported core.");
        var pile = ItemSnapshot.FromCore(InventoryItemCore.Decode(item.Core), true) with { Count = item.Count };
        if (pile.ItemId != item.ItemId) throw new InvalidDataException("Custody disagrees with its pile core.");
        Validate(pile);
        return pile;
    }

    private static CustodyItem ReadCustodyItem(SqliteDataReader reader, int offset) => new(
        reader.GetString(offset), reader.GetInt64(offset + 1), reader.GetInt32(offset + 2), reader.GetInt32(offset + 3),
        reader.IsDBNull(offset + 4) ? null : (byte[])reader.GetValue(offset + 4));
}
