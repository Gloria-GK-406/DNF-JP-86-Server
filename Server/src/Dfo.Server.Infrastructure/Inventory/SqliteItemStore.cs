using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Inventory;

/// <summary>
/// The store whose items are rows in the character's own database.
/// </summary>
/// <remarks>
/// Uses the tables the inventory repositories already create; it defines none of its own. Its
/// session is the one participant in a use case whose commit can still fail, which is what limits
/// a transaction to one of these.
/// </remarks>
internal sealed class SqliteItemStore : IItemStore
{
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
        return transaction.Enlist(
            this.StoreIdentity,
            () => new SqliteItemStoreSession(this.connectionString));
    }
}

/// <summary>One use case's reads and writes against the character database.</summary>
/// <remarks>
/// Writes are held here until the transaction prepares, so that everything the use case decided
/// reaches the database as one statement batch inside one database transaction. Reads consult the
/// held writes first, which is what lets the use case read back what it has just decided without
/// anyone outside seeing it.
///
/// A slot is written by removing whatever row held it and inserting the new one. That gives the
/// item a new row identity, which is deliberate and safe: nothing outside these tables is keyed on
/// it, and the one satellite table there is hangs off the avatar UID carried inside the item core.
/// </remarks>
internal sealed class SqliteItemStoreSession(string connectionString)
    : IItemStoreSession, ITransactionParticipant
{
    /// <summary>What each named slot will hold, or null where it will be left empty.</summary>
    private readonly Dictionary<ContainerId, Dictionary<int, StoredItem?>> staged = [];

    private SqliteConnection? connection;

    private SqliteTransaction? pending;

    private bool prepared;

    public bool CommitCanFail => true;

    public async ValueTask<IReadOnlyList<StoredItem>> ReadAllAsync(
        ContainerId containerId, CancellationToken cancellationToken)
    {
        var owner = OwnerOf(containerId);
        var items = new SortedDictionary<int, StoredItem>();
        var link = await this.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = this.Command(link))
        {
            command.CommandText = """
                SELECT slot_index, item_core
                FROM character_new_items
                WHERE owner_scope = 'character' AND owner_id = @owner
                  AND character_id = @owner AND list_type = @list;
                """;
            command.Parameters.AddWithValue("@owner", owner.Character);
            command.Parameters.AddWithValue("@list", owner.ListType);
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

        var owner = OwnerOf(containerId);
        var link = await this.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = this.Command(link);
        command.CommandText = """
            SELECT item_core
            FROM character_new_items
            WHERE owner_scope = 'character' AND owner_id = @owner
              AND character_id = @owner AND list_type = @list AND slot_index = @slot;
            """;
        command.Parameters.AddWithValue("@owner", owner.Character);
        command.Parameters.AddWithValue("@list", owner.ListType);
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
    /// Every held write is applied here, inside a database transaction that is left open. What
    /// remains for the commit is the one act that can still fail on its own.
    ///
    /// Every slot named here is emptied before any is filled. Each insert then lands on a slot this
    /// same batch has already cleared, whatever the writes happen to be -- so no write can meet the
    /// row it is replacing, and a swap needs nowhere to park.
    ///
    /// The marker is set last: a prepare that threw partway must leave a commit unable to stand in
    /// for it, and the coordinator's rollback is what undoes the statements already applied.
    /// </remarks>
    public async ValueTask PrepareAsync(CancellationToken cancellationToken)
    {
        if (this.staged.Count == 0)
        {
            this.prepared = true;
            return;
        }

        var link = await this.OpenAsync(cancellationToken).ConfigureAwait(false);
        this.pending = (SqliteTransaction)await link
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var (containerId, slots) in this.staged)
        {
            var owner = OwnerOf(containerId);
            foreach (var slot in slots.Keys)
            {
                await this.EmptyAsync(link, owner, slot, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var (containerId, slots) in this.staged)
        {
            var owner = OwnerOf(containerId);
            foreach (var item in slots.Values)
            {
                if (item is not null)
                {
                    await this.FillAsync(link, owner, item, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        this.prepared = true;
    }

    /// <remarks>
    /// A commit reached without a prepare is refused rather than quietly carrying out the writes
    /// itself. Doing them here would make this the second act of the use case that can fail, which
    /// is what the one-fallible-participant rule exists to prevent, and dropping them silently
    /// would surface as lost items rather than as an exception.
    /// </remarks>
    public async ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        if (!this.prepared && this.staged.Count > 0)
        {
            await this.SettleAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                "The item store was committed before it was prepared, so it wrote nothing.");
        }

        try
        {
            if (this.pending is not null)
            {
                await this.pending.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await this.SettleAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask RollbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (this.pending is not null)
            {
                await this.pending.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await this.SettleAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Lets go of everything this session held, however it ended.</summary>
    /// <remarks>
    /// Disposing an uncompleted transaction rolls it back, which can itself fail on a connection
    /// that has already broken. The connection is closed regardless, because a session that cannot
    /// tidy up is exactly the one whose handle must not be left held.
    /// </remarks>
    private async ValueTask SettleAsync()
    {
        try
        {
            if (this.pending is not null)
            {
                await this.pending.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            this.pending = null;
            this.staged.Clear();
            this.prepared = false;
            await this.CloseAsync().ConfigureAwait(false);
        }
    }

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
        SqliteConnection link,
        (int Character, int ListType) owner,
        int slotIndex,
        CancellationToken cancellationToken)
    {
        await using var command = this.Command(link);
        // Named exactly as the table's unique key, so nothing the insert could collide with is
        // left behind -- a narrower predicate would spare such a row and fail the insert instead.
        command.CommandText = """
            DELETE FROM character_new_items
            WHERE owner_scope = 'character' AND owner_id = @owner
              AND list_type = @list AND slot_index = @slot;
            """;
        command.Parameters.AddWithValue("@owner", owner.Character);
        command.Parameters.AddWithValue("@list", owner.ListType);
        command.Parameters.AddWithValue("@slot", slotIndex);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask FillAsync(
        SqliteConnection link,
        (int Character, int ListType) owner,
        StoredItem item,
        CancellationToken cancellationToken)
    {
        await using var command = this.Command(link);
        command.CommandText = """
            INSERT INTO character_new_items(
                owner_scope, owner_id, character_id, list_type, slot_index, item_core)
            VALUES('character', @owner, @owner, @list, @slot, @core);
            """;
        command.Parameters.AddWithValue("@owner", owner.Character);
        command.Parameters.AddWithValue("@list", owner.ListType);
        command.Parameters.AddWithValue("@slot", item.SlotIndex);
        command.Parameters.AddWithValue(
            "@core", SqliteInventoryEntryRepository.EncodeItemCore(item.Core));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (this.connection is not null)
        {
            return this.connection;
        }

        var link = new SqliteConnection(connectionString);
        await link.OpenAsync(cancellationToken).ConfigureAwait(false);
        this.connection = link;
        return link;
    }

    private async ValueTask CloseAsync()
    {
        if (this.connection is null)
        {
            return;
        }

        await this.connection.DisposeAsync().ConfigureAwait(false);
        this.connection = null;
    }

    private SqliteCommand Command(SqliteConnection link)
    {
        var command = link.CreateCommand();
        command.Transaction = this.pending;
        return command;
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
