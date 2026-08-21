using System.Buffers.Binary;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

/// <summary>The <c>character_new_items</c> table.</summary>
/// <remarks>
/// Three reads rather than one, because three callers want three different orders and the order is
/// part of what each of them means: the entry load reads every list by container and slot, the
/// client's drawing order is slot then id within the main list, and a refill walks the main list by
/// slot alone. Collapsing them into one read and sorting afterwards would move a decision about
/// what the client sees into code that is only about rows.
/// </remarks>
internal sealed class SqliteCharacterItemTable(SqliteDatabase database) : ICharacterItemTable
{
    public ValueTask<IReadOnlyList<CharacterItemRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT item_uid, owner_scope, owner_id, character_id,
                   list_type, slot_index, item_core
            FROM character_new_items
            WHERE character_id = @characterId
            ORDER BY list_type, slot_index, item_uid;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            reader => new CharacterItemRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                InventoryItemCore.Decode(EntryCore(reader, 6, reader.GetInt64(0)))),
            cancellationToken);

    public ValueTask<IReadOnlyList<MainItemRow>> ReadMainAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT item_uid,slot_index,item_core FROM character_new_items "
            + "WHERE character_id=@cid AND list_type=0 ORDER BY slot_index,item_uid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new MainItemRow(
                reader.GetInt64(0),
                reader.GetInt32(1),
                InventoryItemCore.Decode(
                    PackedCore(reader, 2, "Inventory item core is malformed."))),
            cancellationToken);

    public ValueTask<IReadOnlyList<MainStackRow>> ReadMainStacksAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT item_uid,slot_index,item_core FROM character_new_items "
            + "WHERE character_id=@cid AND list_type=0 ORDER BY slot_index",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader =>
            {
                var core = InventoryItemCore.Decode(
                    PackedCore(reader, 2, "Daily refill item core is malformed."));
                return new MainStackRow(
                    reader.GetInt64(0), reader.GetInt32(1), core.ItemId, core.Value);
            },
            cancellationToken);

    public async ValueTask<IReadOnlySet<int>> ReadMainOccupiedSlotsAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        new HashSet<int>(await database.ReadAllAsync(
            "SELECT slot_index FROM character_new_items WHERE character_id=@cid AND list_type=0",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => reader.GetInt32(0),
            cancellationToken).ConfigureAwait(false));

    /// <remarks>
    /// What to keep goes into the statement text rather than into one parameter, because SQLite
    /// has no parameter that stands for a list. Each of them is still bound, so no value from
    /// outside ever reaches the statement itself.
    /// </remarks>
    public ValueTask<int> DeleteOwnExceptAsync(
        CharacterId characterId, IReadOnlyList<long> keep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keep);
        return database.ExecuteAsync(
            keep.Count == 0
                ? "DELETE FROM character_new_items WHERE character_id=@cid "
                  + "AND owner_scope='character' AND owner_id=@cid"
                : "DELETE FROM character_new_items WHERE character_id=@cid "
                  + "AND owner_scope='character' AND owner_id=@cid AND item_uid NOT IN ("
                  + string.Join(',', keep.Select((_, index) => $"@p{index}")) + ")",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                for (var index = 0; index < keep.Count; index++)
                {
                    parameters.AddWithValue($"@p{index}", keep[index]);
                }
            },
            cancellationToken);
    }

    public ValueTask<int> UpdateOwnAsync(
        long itemUid,
        CharacterId characterId,
        int listType,
        int slotIndex,
        InventoryItemCoreState core,
        CancellationToken cancellationToken) =>
        database.ExecuteAsync(
            """
            UPDATE character_new_items SET list_type=@list, slot_index=@slot,
                item_core=@core, updated_at=CURRENT_TIMESTAMP
            WHERE item_uid=@uid AND character_id=@cid AND owner_scope='character' AND owner_id=@cid
            """,
            parameters =>
            {
                parameters.AddWithValue("@list", listType);
                parameters.AddWithValue("@slot", slotIndex);
                parameters.AddWithValue("@core", InventoryItemCore.Encode(core));
                parameters.AddWithValue("@uid", itemUid);
                parameters.AddWithValue("@cid", characterId.Value);
            },
            cancellationToken);

    public ValueTask<int> InsertOwnAsync(
        CharacterId characterId,
        int listType,
        int slotIndex,
        InventoryItemCoreState core,
        CancellationToken cancellationToken) =>
        database.ExecuteAsync(
            "INSERT INTO character_new_items"
            + "(owner_scope,owner_id,character_id,list_type,slot_index,item_core) "
            + "VALUES('character',@cid,@cid,@list,@slot,@core)",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@list", listType);
                parameters.AddWithValue("@slot", slotIndex);
                parameters.AddWithValue("@core", InventoryItemCore.Encode(core));
            },
            cancellationToken);

    /// <remarks>
    /// The stored form is read back before it is written, rather than composed afresh, so that
    /// everything a stack carries besides how many of it there are survives untouched. Both
    /// statements belong to the use case, so nothing can come between them.
    /// </remarks>
    public async ValueTask<int> SetStackCountAsync(
        long itemUid, int count, CancellationToken cancellationToken)
    {
        var stored = await database.ReadFirstAsync(
            "SELECT item_core FROM character_new_items WHERE item_uid=@uid",
            parameters => parameters.AddWithValue("@uid", itemUid),
            reader => PackedCore(reader, 0, "Daily refill item core is malformed."),
            cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return 0;
        }

        return await database.ExecuteAsync(
            "UPDATE character_new_items SET item_core=@core,updated_at=CURRENT_TIMESTAMP "
            + "WHERE item_uid=@uid",
            parameters =>
            {
                parameters.AddWithValue(
                    "@core",
                    InventoryItemCore.Encode(
                        InventoryItemCore.Decode(stored) with { Value = count }));
                parameters.AddWithValue("@uid", itemUid);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <remarks>
    /// The two values that are neither zero nor asked for -- a marker of minus one and a
    /// changed-option index of 255 -- are what the stored form of a freshly made stack has always
    /// looked like. They stay here beside the packing until an item is broken into columns.
    /// </remarks>
    public ValueTask<int> InsertStackAsync(
        CharacterId characterId,
        int slotIndex,
        int itemId,
        int count,
        byte itemKind,
        CancellationToken cancellationToken)
    {
        var core = new byte[InventoryItemCore.Size];
        core[0] = itemKind;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5, 4), count);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(21, 4), -1);
        core[66] = byte.MaxValue;
        return database.ExecuteAsync(
            "INSERT INTO character_new_items"
            + "(owner_scope,owner_id,character_id,list_type,slot_index,item_core) "
            + "VALUES('character',@cid,@cid,0,@slot,@core)",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@slot", slotIndex);
                parameters.AddWithValue("@core", core);
            },
            cancellationToken);
    }

    /// <summary>The packed bytes, named by the row they came from when they are wrong.</summary>
    internal static byte[] EntryCore(SqliteDataReader reader, int ordinal, long storageEntryId)
    {
        if (reader.IsDBNull(ordinal))
        {
            throw new InvalidDataException($"Inventory row {storageEntryId} has no item core.");
        }

        var core = (byte[])reader.GetValue(ordinal);
        return core.Length == InventoryItemCore.Size
            ? core
            : throw new InvalidDataException(
                $"Inventory row {storageEntryId} item core length is {core.Length}; "
                + $"expected {InventoryItemCore.Size}.");
    }

    private static byte[] PackedCore(SqliteDataReader reader, int ordinal, string whenWrong)
    {
        var core = (byte[])reader.GetValue(ordinal);
        return core.Length == InventoryItemCore.Size
            ? core
            : throw new InvalidDataException(whenWrong);
    }
}

/// <summary>The <c>account_cargo_new_items</c> table.</summary>
internal sealed class SqliteAccountCargoItemTable(SqliteDatabase database) : IAccountCargoItemTable
{
    public ValueTask<IReadOnlyList<AccountCargoItemRow>> ReadByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT item_uid, account_id, character_id, list_type, slot_index, item_core
            FROM account_cargo_new_items
            WHERE account_id = @accountId
            ORDER BY list_type, slot_index, item_uid;
            """,
            parameters => parameters.AddWithValue("@accountId", accountId.Value),
            reader => new AccountCargoItemRow(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                InventoryItemCore.Decode(
                    SqliteCharacterItemTable.EntryCore(reader, 5, reader.GetInt64(0)))),
            cancellationToken);
}

/// <summary>The <c>character_avatar_detail</c> table.</summary>
internal sealed class SqliteCharacterAvatarDetailTable(SqliteDatabase database)
    : ICharacterAvatarDetailTable
{
    public ValueTask<AvatarDetailRow?> FindAsync(
        long itemUid, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT owner_id,character_id,item_id,expire_date,clear_avatar_id,jewel_socket,
                   color1,color2,delete_date
            FROM character_avatar_detail WHERE item_uid=@avatarUid LIMIT 2
            """,
            parameters => parameters.AddWithValue("@avatarUid", itemUid),
            reader => new AvatarDetailRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                (byte[])reader.GetValue(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8)),
            cancellationToken);

    public ValueTask<int> DeleteOwnExceptAsync(
        CharacterId characterId, IReadOnlyList<int> keep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keep);
        return database.ExecuteAsync(
            keep.Count == 0
                ? "DELETE FROM character_avatar_detail WHERE character_id=@cid AND owner_id=@cid"
                : "DELETE FROM character_avatar_detail WHERE character_id=@cid AND owner_id=@cid "
                  + "AND item_uid NOT IN ("
                  + string.Join(',', keep.Select((_, index) => $"@avatar{index}")) + ")",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                for (var index = 0; index < keep.Count; index++)
                {
                    parameters.AddWithValue($"@avatar{index}", keep[index]);
                }
            },
            cancellationToken);
    }

    /// <remarks>
    /// The condition on the update half is what stops one character writing over another's
    /// avatar: a conflicting row belonging to someone else matches nothing and is left alone,
    /// which the caller sees as no row touched.
    /// </remarks>
    public ValueTask<int> UpsertOwnAsync(
        CharacterId characterId,
        int itemUid,
        int itemId,
        InventoryAvatarDetailState detail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return database.ExecuteAsync(
            """
            INSERT INTO character_avatar_detail(item_uid,owner_id,character_id,item_id,expire_date,
              clear_avatar_id,jewel_socket,color1,color2,delete_date)
            VALUES(@uid,@cid,@cid,@item,@expire,@clear,@socket,@color1,@color2,@delete)
            ON CONFLICT(item_uid) DO UPDATE SET owner_id=excluded.owner_id,
              character_id=excluded.character_id,item_id=excluded.item_id,expire_date=excluded.expire_date,
              clear_avatar_id=excluded.clear_avatar_id,jewel_socket=excluded.jewel_socket,
              color1=excluded.color1,color2=excluded.color2,delete_date=excluded.delete_date
            WHERE character_avatar_detail.owner_id=@cid
              AND character_avatar_detail.character_id=@cid
            """,
            parameters =>
            {
                parameters.AddWithValue("@uid", itemUid);
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@item", itemId);
                parameters.AddWithValue("@expire", detail.ExpireDate);
                parameters.AddWithValue("@clear", detail.ClearAvatarId);
                parameters.AddWithValue("@socket", detail.JewelSocket);
                parameters.AddWithValue("@color1", detail.Color1);
                parameters.AddWithValue("@color2", detail.Color2);
                parameters.AddWithValue("@delete", detail.DeleteDate);
            },
            cancellationToken);
    }
}
