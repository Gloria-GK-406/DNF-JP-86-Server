using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Persistence.Inventory;

/// <summary>Private store custody: one row per laid-out line, kept in the item transaction.</summary>
/// <remarks>
/// The same holding as auction custody: equipment keeps its <c>items</c> row and identity with no
/// position, a pile keeps its exact 82-byte core here and its item row goes.
/// </remarks>
internal sealed partial class SqliteItemDomainStoreSession : IPrivateStoreItemCustodySession
{
    public async ValueTask AcceptPrivateStoreLineAsync(CharacterId seller, byte displaySlot, long unitPrice, ItemRecord record,
        CancellationToken ct)
    {
        Active();
        ArgumentNullException.ThrowIfNull(record);
        var foreign = scope.IsForeign(record, identity);
        var uid = foreign ? 0 : Detached(record);
        var snapshot = record.Snapshot;
        Validate(snapshot);
        if (snapshot.AvatarDetail is not null || snapshot.CreatureDetail is not null || snapshot.IsMirror)
            throw new InvalidOperationException("A stall holds only items without instance details.");
        if (!snapshot.IsPile && (foreign || snapshot.ItemUid?.Value != uid))
            throw new InvalidOperationException("Equipment goes on a stall only as the detached record of its own row.");
        if (!foreign && await HasPositionAsync(uid, ct).ConfigureAwait(false))
            throw new InvalidOperationException("A detached item still occupies an inventory position.");
        await Mutate(async () =>
        {
            await using (var command = await owner.CommandAsync(ct).ConfigureAwait(false))
            {
                command.CommandText = """
                    INSERT INTO private_store_custody(owner_id,display_slot,unit_price,kind,item_uid,item_id,item_count,item_core)
                    VALUES(@owner,@slot,@price,@kind,@uid,@item,@count,@core)
                    """;
                command.Parameters.AddWithValue("@owner", seller.Value);
                command.Parameters.AddWithValue("@slot", (int)displaySlot);
                command.Parameters.AddWithValue("@price", unitPrice);
                command.Parameters.AddWithValue("@kind", snapshot.IsPile ? "stackable" : "equipment");
                command.Parameters.AddWithValue("@uid", snapshot.IsPile ? 0 : uid);
                command.Parameters.AddWithValue("@item", snapshot.ItemId);
                command.Parameters.AddWithValue("@count", snapshot.Count);
                command.Parameters.AddWithValue("@core", snapshot.IsPile ? InventoryItemCore.Encode(snapshot.ToCore()) : DBNull.Value);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            if (snapshot.IsPile && uid != 0) await RemoveRowAsync(uid, ct).ConfigureAwait(false);
            await scope.AcceptAsync(record, identity, ct).ConfigureAwait(false);
            detached.Remove(record);
            return true;
        }).ConfigureAwait(false);
    }

    public async ValueTask<ItemRecord> TakePrivateStoreLineAsync(CharacterId seller, byte displaySlot, int? count, CancellationToken ct)
    {
        Active();
        var line = await ReadLineAsync(seller, displaySlot, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The stall has no such line.");
        var amount = count ?? line.Count;
        if (amount <= 0 || amount > line.Count || line.Kind == "equipment" && amount != 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (line.Kind == "equipment")
        {
            var snapshot = await EquipmentAsync(line, ct).ConfigureAwait(false);
            return await Mutate(async () =>
            {
                await DeleteLineAsync(seller, displaySlot, ct).ConfigureAwait(false);
                return Issue(line.Uid, snapshot);
            }).ConfigureAwait(false);
        }
        var pile = PileOf(line);
        return await Mutate(async () =>
        {
            if (amount == line.Count) await DeleteLineAsync(seller, displaySlot, ct).ConfigureAwait(false);
            else
            {
                await using var update = await owner.CommandAsync(ct).ConfigureAwait(false);
                update.CommandText = """
                    UPDATE private_store_custody SET item_count=@count
                    WHERE owner_id=@owner AND display_slot=@slot AND item_count=@held
                    """;
                update.Parameters.AddWithValue("@count", line.Count - amount);
                update.Parameters.AddWithValue("@held", line.Count);
                update.Parameters.AddWithValue("@owner", seller.Value);
                update.Parameters.AddWithValue("@slot", (int)displaySlot);
                if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Stall custody changed during a take.");
            }
            var piece = pile with { Count = amount };
            return Issue(await InsertAsync(piece, ct).ConfigureAwait(false), piece);
        }).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<PrivateStoreLine>> ReadPrivateStoreLinesAsync(CharacterId seller, CancellationToken ct)
    {
        Active();
        var rows = new List<CustodyLine>();
        await using (var command = await owner.CommandAsync(ct).ConfigureAwait(false))
        {
            command.CommandText = """
                SELECT display_slot,unit_price,kind,item_uid,item_id,item_count,item_core
                FROM private_store_custody WHERE owner_id=@owner ORDER BY display_slot
                """;
            command.Parameters.AddWithValue("@owner", seller.Value);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) rows.Add(ReadLine(reader));
        }
        var lines = new List<PrivateStoreLine>(rows.Count);
        foreach (var row in rows)
            lines.Add(new(row.Slot, row.UnitPrice, row.Kind == "equipment"
                ? await EquipmentAsync(row, ct).ConfigureAwait(false)
                : PileOf(row) with { Count = row.Count }));
        return lines;
    }

    private sealed record CustodyLine(byte Slot, long UnitPrice, string Kind, long Uid, int ItemId, int Count, byte[]? Core);

    private async ValueTask<CustodyLine?> ReadLineAsync(CharacterId ownerId, byte slot, CancellationToken ct)
    {
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = """
            SELECT display_slot,unit_price,kind,item_uid,item_id,item_count,item_core
            FROM private_store_custody WHERE owner_id=@owner AND display_slot=@slot
            """;
        command.Parameters.AddWithValue("@owner", ownerId.Value);
        command.Parameters.AddWithValue("@slot", (int)slot);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadLine(reader) : null;
    }

    private static CustodyLine ReadLine(Microsoft.Data.Sqlite.SqliteDataReader r) => new(
        checked((byte)r.GetInt32(0)), r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetInt32(4), r.GetInt32(5),
        r.IsDBNull(6) ? null : (byte[])r.GetValue(6));

    private async ValueTask<ItemSnapshot> EquipmentAsync(CustodyLine line, CancellationToken ct)
    {
        if (await HasPositionAsync(line.Uid, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Equipment on a stall also occupies a position.");
        var snapshot = await ReadMailWorldAsync(checked((int)line.Uid), ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Equipment on a stall has no item row.");
        if (snapshot.ItemId != line.ItemId || snapshot.IsPile)
            throw new InvalidDataException("Stall custody disagrees with its equipment row.");
        return snapshot;
    }

    private static ItemSnapshot PileOf(CustodyLine line)
    {
        if (line.Core is not { Length: InventoryItemCore.Size })
            throw new InvalidDataException("Stall pile custody has no core.");
        var pile = ItemSnapshot.FromCore(InventoryItemCore.Decode(line.Core), true);
        if (pile.ItemId != line.ItemId) throw new InvalidDataException("Stall custody disagrees with its pile core.");
        Validate(pile with { Count = line.Count });
        return pile;
    }

    private async ValueTask DeleteLineAsync(CharacterId ownerId, byte slot, CancellationToken ct)
    {
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = "DELETE FROM private_store_custody WHERE owner_id=@owner AND display_slot=@slot";
        command.Parameters.AddWithValue("@owner", ownerId.Value);
        command.Parameters.AddWithValue("@slot", (int)slot);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Stall custody disappeared.");
    }
}
