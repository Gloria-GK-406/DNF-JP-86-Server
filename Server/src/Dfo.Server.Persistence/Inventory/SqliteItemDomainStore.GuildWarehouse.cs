using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

internal sealed partial class SqliteItemDomainStoreSession : IGuildWarehouseCustodySession
{
    public async ValueTask<ItemSnapshot?> ReadSlotAsync(GuildId guild, int slot, CancellationToken ct)
    {
        Active();
        var row = await new GuildCustodyStorage(owner, guild, slot).FindAsync(ct).ConfigureAwait(false);
        return row is null ? null : await CustodySnapshotAsync(row, ct).ConfigureAwait(false);
    }

    public async ValueTask DepositAsync(GuildId guild, int slot, ItemRecord record, ItemSnapshot? occupied, CancellationToken ct)
    {
        if (occupied is null)
        {
            await AcceptCustodyAsync(new GuildCustodyStorage(owner, guild, slot), record, ct).ConfigureAwait(false);
            return;
        }

        Active();
        if (!occupied.CanStackWith(record.Snapshot))
            throw new InvalidOperationException("The guild warehouse slot cannot accept this stack.");

        await Mutate(async () =>
        {
            await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
            command.CommandText = """
                UPDATE guild_warehouse_items SET item_count=@total
                WHERE guild_id=@guild AND slot_index=@slot AND kind='stackable' AND item_count=@held AND item_id=@item
                """;
            command.Parameters.AddWithValue("@guild", (long)guild.Value);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@total", checked(occupied.Count + record.Snapshot.Count));
            command.Parameters.AddWithValue("@held", occupied.Count);
            command.Parameters.AddWithValue("@item", occupied.ItemId);
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The guild warehouse stack changed during deposit.");
            await DeleteItemAsync(record, ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    public ValueTask<ItemRecord> WithdrawAsync(GuildId guild, int slot, int count, CancellationToken ct) =>
        TakeCustodyAsync(new GuildCustodyStorage(owner, guild, slot), count, ct);

    private sealed class GuildCustodyStorage(SqliteDatabaseParticipant participant, GuildId guild, int slot)
        : CustodyStorage(participant)
    {
        protected override string Table => "guild_warehouse_items";
        protected override string Key => "guild_id=@guild AND slot_index=@slot";
        protected override string InsertColumns => "guild_id,slot_index";
        protected override string InsertValues => "@guild,@slot";

        protected override void Bind(SqliteCommand command)
        {
            command.Parameters.AddWithValue("@guild", (long)guild.Value);
            command.Parameters.AddWithValue("@slot", slot);
        }
    }
}
