using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

internal sealed class SqliteEquipmentTransferDomainStore : IEquipmentTransferDomainStore
{
    private static readonly object FacetKey = typeof(SqliteEquipmentTransferDomainStoreSession);
    private readonly string connectionString;

    public SqliteEquipmentTransferDomainStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        this.connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
    }

    public object StoreIdentity => this.connectionString;

    public bool IsDurable => true;

    public IEquipmentTransferDomainStoreSession SessionIn(IDomainTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var database = transaction.Enlist(
            this.StoreIdentity, () => new SqliteDatabaseParticipant(this.connectionString));
        return database.Facet(FacetKey, owner => new SqliteEquipmentTransferDomainStoreSession(owner));
    }
}

/// <summary>Moves only positions; the world row and all its instance columns remain untouched.</summary>
internal sealed class SqliteEquipmentTransferDomainStoreSession(SqliteDatabaseParticipant owner)
    : IEquipmentTransferDomainStoreSession, ISqliteStoreFacet
{
    public bool HasStagedWork => false;

    public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public void Discard() { }

    public async ValueTask<EquipmentTransferItem?> ReadOwnedAsync(
        CharacterId characterId, ItemIdentity identity, CancellationToken cancellationToken)
    {
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            SELECT place.list_type, place.slot_index, held.item_core
            FROM item_slots place JOIN items held ON held.item_uid = place.core_uid
            WHERE place.owner_scope = 'character' AND place.owner_id = @owner
              AND place.kind = 'world' AND place.id = @uid
              AND place.core_uid = @uid AND held.item_uid = @uid
              AND (SELECT COUNT(*) FROM item_slots WHERE core_uid = @uid) = 1;
            """;
        command.Parameters.AddWithValue("@owner", characterId.Value);
        command.Parameters.AddWithValue("@uid", identity.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        var listType = reader.GetInt32(0);
        if (listType is < 0 or > byte.MaxValue
            || !Enum.IsDefined((InventoryContainerType)listType))
            throw new InvalidDataException($"Item {identity.Value} has invalid list type {listType}.");
        var item = new EquipmentTransferItem(identity, (InventoryContainerType)listType,
            reader.GetInt32(1), InventoryItemCore.Decode((byte[])reader.GetValue(2)));
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException($"Item {identity.Value} occupies more than one slot.");
        return item;
    }

    public async ValueTask<InventoryItemCoreState?> ReadDetachedAsync(
        ItemIdentity identity, CancellationToken cancellationToken)
    {
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            SELECT held.item_core FROM items held
            WHERE held.item_uid = @uid AND NOT EXISTS (
                SELECT 1 FROM item_slots place WHERE place.core_uid = held.item_uid);
            """;
        command.Parameters.AddWithValue("@uid", identity.Value);
        var core = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return core is byte[] bytes ? InventoryItemCore.Decode(bytes) : null;
    }

    public async ValueTask<bool> TakeAsync(
        CharacterId characterId, EquipmentTransferItem item, CancellationToken cancellationToken)
    {
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            DELETE FROM item_slots
            WHERE owner_scope = 'character' AND owner_id = @owner
              AND list_type = @list AND slot_index = @slot
              AND kind = 'world' AND id = @uid AND core_uid = @uid
              AND (SELECT COUNT(*) FROM item_slots WHERE core_uid = @uid) = 1;
            """;
        command.Parameters.AddWithValue("@owner", characterId.Value);
        command.Parameters.AddWithValue("@list", (int)item.ContainerType);
        command.Parameters.AddWithValue("@slot", item.SlotIndex);
        command.Parameters.AddWithValue("@uid", item.Identity.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async ValueTask<bool> GrantAsync(
        CharacterId characterId, ItemIdentity identity, GameSlot slot, CancellationToken cancellationToken)
    {
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            INSERT INTO item_slots(owner_scope, owner_id, list_type, slot_index, kind, id, core_uid, count)
            SELECT 'character', @owner, @list, @slot, 'world', held.item_uid, held.item_uid, 1
            FROM items held
            WHERE held.item_uid = @uid
              AND EXISTS (SELECT 1 FROM characters WHERE character_id = @owner)
              AND NOT EXISTS (SELECT 1 FROM item_slots WHERE core_uid = @uid)
              AND NOT EXISTS (
                  SELECT 1 FROM item_slots WHERE owner_scope = 'character'
                    AND owner_id = @owner AND list_type = @list AND slot_index = @slot);
            """;
        command.Parameters.AddWithValue("@owner", characterId.Value);
        command.Parameters.AddWithValue("@list", (int)slot.Space);
        command.Parameters.AddWithValue("@slot", slot.Index);
        command.Parameters.AddWithValue("@uid", identity.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }
}
