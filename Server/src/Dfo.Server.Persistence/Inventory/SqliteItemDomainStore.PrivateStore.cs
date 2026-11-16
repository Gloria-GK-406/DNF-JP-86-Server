using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

internal sealed partial class SqliteItemDomainStoreSession : IPrivateStoreItemCustodySession
{
    public ValueTask AcceptPrivateStoreLineAsync(CharacterId seller, byte displaySlot, long unitPrice, ItemRecord record,
        CancellationToken ct) => AcceptCustodyAsync(new StallCustodyStorage(owner, seller, displaySlot, unitPrice), record, ct);

    public ValueTask<ItemRecord> TakePrivateStoreLineAsync(CharacterId seller, byte displaySlot, int? count, CancellationToken ct) =>
        TakeCustodyAsync(new StallCustodyStorage(owner, seller, displaySlot, 0), count, ct);

    public async ValueTask<IReadOnlyList<PrivateStoreLine>> ReadPrivateStoreLinesAsync(CharacterId seller, CancellationToken ct)
    {
        Active();
        var rows = new List<(byte Slot, long Price, CustodyItem Item)>();
        await using (var command = await owner.CommandAsync(ct).ConfigureAwait(false))
        {
            command.CommandText = """
                SELECT display_slot,unit_price,kind,item_uid,item_id,item_count,item_core
                FROM private_store_custody WHERE owner_id=@owner ORDER BY display_slot
                """;
            command.Parameters.AddWithValue("@owner", seller.Value);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                rows.Add((checked((byte)reader.GetInt32(0)), reader.GetInt64(1), ReadCustodyItem(reader, 2)));
        }
        var lines = new List<PrivateStoreLine>(rows.Count);
        foreach (var row in rows)
            lines.Add(new(row.Slot, row.Price, await CustodySnapshotAsync(row.Item, ct).ConfigureAwait(false)));
        return lines;
    }

    private sealed class StallCustodyStorage(SqliteDatabaseParticipant participant, CharacterId seller, byte slot, long price)
        : CustodyStorage(participant)
    {
        protected override string Table => "private_store_custody";
        protected override string Key => "owner_id=@owner AND display_slot=@slot";
        protected override string InsertColumns => "owner_id,display_slot,unit_price";
        protected override string InsertValues => "@owner,@slot,@price";
        protected override void Bind(SqliteCommand command)
        {
            command.Parameters.AddWithValue("@owner", seller.Value);
            command.Parameters.AddWithValue("@slot", (int)slot);
            command.Parameters.AddWithValue("@price", price);
        }
    }
}
