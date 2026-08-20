using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

/// <summary>
/// The store whose items are rows in the character's own database.
/// </summary>
/// <remarks>
/// Uses the tables the inventory repositories already create; it defines none of its own.
///
/// What takes part in the transaction is the database, not this store. Items are one of several
/// kinds of state kept there, and a transaction admits only one participant whose commit can still
/// fail -- so a use case that changed items and something else would be refused if each joined
/// separately. This store therefore contributes a facet to the database's participation.
/// </remarks>
internal sealed class SqliteItemStore : IItemStore
{
    /// <summary>What the database registers this store's session under.</summary>
    private static readonly object FacetKey = typeof(SqliteItemStoreSession);

    private readonly string connectionString;

    public SqliteItemStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        this.connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
    }

    public object StoreIdentity => this.connectionString;

    public bool IsDurable => true;

    public IItemStoreSession SessionIn(IDomainTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var database = transaction.Enlist(
            this.StoreIdentity,
            () => new SqliteDatabaseParticipant(this.connectionString));
        return database.Facet(FacetKey, owner => new SqliteItemStoreSession(owner));
    }
}

/// <summary>One use case's reads and writes against the items in the character database.</summary>
/// <remarks>
/// Writes are held here until the database prepares, so that everything the use case decided
/// reaches it as one statement batch inside one database transaction. Reads consult the held
/// writes first, which is what lets the use case read back what it has just decided without
/// anyone outside seeing it.
///
/// A slot is written by removing whatever row held it and inserting the new one. That gives the
/// item a new row identity, which is deliberate and safe: nothing outside these tables is keyed on
/// it, and the one satellite table there is hangs off the avatar UID carried inside the item core.
/// </remarks>
internal sealed class SqliteItemStoreSession(SqliteDatabaseParticipant owner)
    : IItemStoreSession, ISqliteStoreFacet
{
    /// <summary>What each named slot will hold, or null where it will be left empty.</summary>
    private readonly Dictionary<ContainerId, Dictionary<int, StoredItem?>> staged = [];

    public bool HasStagedWork => this.staged.Count > 0;

    public async ValueTask<IReadOnlyList<StoredItem>> ReadAllAsync(
        ContainerId containerId, CancellationToken cancellationToken)
    {
        var place = OwnerOf(containerId);
        var items = new SortedDictionary<int, StoredItem>();
        await using (var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false))
        {
            command.CommandText = """
                SELECT slot_index, item_core
                FROM character_new_items
                WHERE owner_scope = 'character' AND owner_id = @owner
                  AND character_id = @owner AND list_type = @list;
                """;
            command.Parameters.AddWithValue("@owner", place.Character);
            command.Parameters.AddWithValue("@list", place.ListType);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var slot = reader.GetInt32(0);
                items[slot] = new StoredItem(slot, DecodeCore(reader, 1));
            }
        }

        if (this.staged.TryGetValue(containerId, out var slots))
        {
            foreach (var (slot, item) in slots)
            {
                if (item is null)
                {
                    items.Remove(slot);
                }
                else
                {
                    items[slot] = item;
                }
            }
        }

        return [.. items.Values];
    }

    public async ValueTask<StoredItem?> ReadAsync(
        ContainerId containerId, int slotIndex, CancellationToken cancellationToken)
    {
        if (this.staged.TryGetValue(containerId, out var slots)
            && slots.TryGetValue(slotIndex, out var held))
        {
            return held;
        }

        var place = OwnerOf(containerId);
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            SELECT item_core
            FROM character_new_items
            WHERE owner_scope = 'character' AND owner_id = @owner
              AND character_id = @owner AND list_type = @list AND slot_index = @slot;
            """;
        command.Parameters.AddWithValue("@owner", place.Character);
        command.Parameters.AddWithValue("@list", place.ListType);
        command.Parameters.AddWithValue("@slot", slotIndex);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredItem(slotIndex, DecodeCore(reader, 0))
            : null;
    }

    public void Write(ContainerId containerId, StoredItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _ = OwnerOf(containerId);
        ValidateSlot(item.SlotIndex);
        this.Staged(containerId)[item.SlotIndex] = item;
    }

    public void Clear(ContainerId containerId, int slotIndex)
    {
        _ = OwnerOf(containerId);
        ValidateSlot(slotIndex);
        this.Staged(containerId)[slotIndex] = null;
    }

    /// <remarks>
    /// Every slot named here is emptied before any is filled. Each insert then lands on a slot this
    /// same batch has already cleared, whatever the writes happen to be -- so no write can meet the
    /// row it is replacing, and a swap needs nowhere to park.
    /// </remarks>
    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        foreach (var (containerId, slots) in this.staged)
        {
            var place = OwnerOf(containerId);
            foreach (var slot in slots.Keys)
            {
                await this.EmptyAsync(place, slot, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var (containerId, slots) in this.staged)
        {
            var place = OwnerOf(containerId);
            foreach (var item in slots.Values)
            {
                if (item is not null)
                {
                    await this.FillAsync(place, item, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    public void Discard() => this.staged.Clear();

    private Dictionary<int, StoredItem?> Staged(ContainerId containerId)
    {
        if (!this.staged.TryGetValue(containerId, out var slots))
        {
            slots = [];
            this.staged.Add(containerId, slots);
        }

        return slots;
    }

    private async ValueTask EmptyAsync(
        (int Character, int ListType) place,
        int slotIndex,
        CancellationToken cancellationToken)
    {
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        // Named exactly as the table's unique key, so nothing the insert could collide with is
        // left behind -- a narrower predicate would spare such a row and fail the insert instead.
        command.CommandText = """
            DELETE FROM character_new_items
            WHERE owner_scope = 'character' AND owner_id = @owner
              AND list_type = @list AND slot_index = @slot;
            """;
        command.Parameters.AddWithValue("@owner", place.Character);
        command.Parameters.AddWithValue("@list", place.ListType);
        command.Parameters.AddWithValue("@slot", slotIndex);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask FillAsync(
        (int Character, int ListType) place,
        StoredItem item,
        CancellationToken cancellationToken)
    {
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            INSERT INTO character_new_items(
                owner_scope, owner_id, character_id, list_type, slot_index, item_core)
            VALUES('character', @owner, @owner, @list, @slot, @core);
            """;
        command.Parameters.AddWithValue("@owner", place.Character);
        command.Parameters.AddWithValue("@list", place.ListType);
        command.Parameters.AddWithValue("@slot", item.SlotIndex);
        command.Parameters.AddWithValue(
            "@core", SqliteInventoryEntryRepository.EncodeItemCore(item.Core));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static InventoryItemCoreState DecodeCore(SqliteDataReader reader, int ordinal) =>
        SqliteInventoryEntryRepository.DecodeItemCore((byte[])reader.GetValue(ordinal));

    /// <summary>The slot numbers the table will accept.</summary>
    /// <remarks>
    /// The implementation this replaces parked a row on slot -1 for the length of a swap. Nothing
    /// does that any more, and a negative slot written now would be discovered only when the
    /// character next tried to enter and the row failed validation.
    /// </remarks>
    private static void ValidateSlot(int slotIndex)
    {
        if (slotIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex), slotIndex, "A slot of a container is never negative.");
        }
    }

    /// <remarks>
    /// A container identifier this store cannot place is refused rather than silently read as
    /// empty, which would let a use case believe a container it cannot reach is simply unfilled.
    ///
    /// Account cargo is refused along with the rest: it is a character container type by name only,
    /// and its rows live in another table keyed by account. Writing it here would leave a row this
    /// server rejects on the character's next entry -- a corruption found as a failure to log in.
    /// </remarks>
    private static (int Character, int ListType) OwnerOf(ContainerId containerId)
    {
        if (!containerId.TryGetCharacterOwner(out var characterId, out var containerType))
        {
            throw new InvalidOperationException(
                $"Container '{containerId.Value}' is not a character's, so this store holds none of it.");
        }

        if (containerType is not (InventoryContainerType.Main
            or InventoryContainerType.Avatar
            or InventoryContainerType.PersonalCargo
            or InventoryContainerType.Equipment
            or InventoryContainerType.Pet))
        {
            throw new InvalidOperationException(
                $"Container '{containerId.Value}' is not one this store keeps rows for.");
        }

        return (characterId.Value, (int)containerType);
    }
}
