using System.Buffers.Binary;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

/// <summary>The rows of <c>items</c> a character owns.</summary>
/// <remarks>
/// Three reads rather than one, because three callers want three different orders and the order is
/// part of what each of them means: the entry load reads every list by container and slot, the
/// client's drawing order is slot then id within the main list, and a refill walks the main list by
/// slot alone. Collapsing them into one read and sorting afterwards would move a decision about
/// what the client sees into code that is only about rows.
///
/// A character's inventory stops short of the title-book containers. They are rows of this table
/// like any other, but the load they feed parses a container type that admits only 0, 1, 2, 3 and
/// 7, so one reaching it would be refused rather than ignored.
/// </remarks>
internal sealed class SqliteCharacterItemTable(SqliteDatabase database) : ICharacterItemTable
{
    /// <summary>What the load reads, in the order it reads it.</summary>
    /// <remarks>
    /// The avatar columns come with the row now. Whether a row has them is decided by whether the
    /// avatar UID is there, which is set on exactly the rows that are avatars.
    /// </remarks>
    public ValueTask<IReadOnlyList<CharacterItemRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT item_uid, owner_scope, owner_id, list_type, slot_index, item_core,
                   avatar_uid, avatar_expire_date, avatar_clear_id, avatar_jewel_socket,
                   avatar_color1, avatar_color2, avatar_delete_date
            FROM items
            WHERE owner_scope = 'character' AND owner_id = @characterId
              AND list_type NOT BETWEEN 19 AND 23
            ORDER BY list_type, slot_index, item_uid;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            reader => new CharacterItemRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                InventoryItemCore.Decode(EntryCore(reader, 5, reader.GetInt64(0))),
                Avatar(reader, 6)),
            cancellationToken);

    public ValueTask<IReadOnlyList<MainItemRow>> ReadMainAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT item_uid,slot_index,item_core FROM items "
            + "WHERE owner_scope='character' AND owner_id=@cid AND list_type=0 "
            + "ORDER BY slot_index,item_uid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new MainItemRow(
                reader.GetInt64(0),
                reader.GetInt32(1),
                InventoryItemCore.Decode(
                    PackedCore(reader, 2, "Inventory item core is malformed."))),
            cancellationToken);

    /// <remarks>
    /// What to keep goes into the statement text rather than into one parameter, because SQLite
    /// has no parameter that stands for a list. Each of them is still bound, so no value from
    /// outside ever reaches the statement itself.
    ///
    /// A row taken away here takes its avatar facts with it, because they are its own columns.
    ///
    /// The title-book containers are left out, exactly as the read above leaves them out. What is
    /// kept comes from a caller that can only name the five containers an inventory has, so
    /// without this every save would take a character's title book for something it no longer
    /// holds and delete the lot.
    /// </remarks>
    public ValueTask<int> DeleteOwnExceptAsync(
        CharacterId characterId, IReadOnlyList<long> keep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keep);
        return database.ExecuteAsync(
            keep.Count == 0
                ? "DELETE FROM items WHERE owner_scope='character' AND owner_id=@cid "
                  + "AND list_type NOT BETWEEN 19 AND 23"
                : "DELETE FROM items WHERE owner_scope='character' AND owner_id=@cid "
                  + "AND list_type NOT BETWEEN 19 AND 23 AND item_uid NOT IN ("
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

    /// <remarks>
    /// The avatar columns are written on every update rather than only when there are some, so an
    /// item that stops being an avatar stops carrying them. Leaving them would not be a harmless
    /// leftover: an item of another kind arriving with avatar facts is refused by the encoder, and
    /// the refusal would surface a long way from the write that caused it.
    ///
    /// The condition on the avatar UID is what stops one character claiming an avatar another one
    /// holds. It is written as a condition rather than left to the unique index so that the write
    /// simply touches nothing, which the caller already reads as a refusal -- the index raising
    /// would take the same decision out of the hands of the code that owns it.
    /// </remarks>
    public ValueTask<int> UpdateOwnAsync(
        long itemUid,
        CharacterId characterId,
        int listType,
        int slotIndex,
        InventoryItemCoreState core,
        InventoryAvatarDetailState? avatarDetail,
        CancellationToken cancellationToken) =>
        database.ExecuteAsync(
            """
            UPDATE items SET list_type=@list, slot_index=@slot, item_core=@core,
                avatar_uid=@avatarUid, avatar_expire_date=@expire, avatar_clear_id=@clear,
                avatar_jewel_socket=@socket, avatar_color1=@color1, avatar_color2=@color2,
                avatar_delete_date=@delete, updated_at=CURRENT_TIMESTAMP
            WHERE item_uid=@uid AND owner_scope='character' AND owner_id=@cid
              AND NOT EXISTS (
                  SELECT 1 FROM items held
                  WHERE held.avatar_uid = @avatarUid AND held.item_uid <> @uid)
            """,
            parameters =>
            {
                parameters.AddWithValue("@list", listType);
                parameters.AddWithValue("@slot", slotIndex);
                parameters.AddWithValue("@core", InventoryItemCore.Encode(core));
                BindAvatar(parameters, core, avatarDetail);
                parameters.AddWithValue("@uid", itemUid);
                parameters.AddWithValue("@cid", characterId.Value);
            },
            cancellationToken);

    public ValueTask<int> InsertOwnAsync(
        CharacterId characterId,
        int listType,
        int slotIndex,
        InventoryItemCoreState core,
        InventoryAvatarDetailState? avatarDetail,
        CancellationToken cancellationToken) =>
        database.ExecuteAsync(
            "INSERT INTO items"
            + "(owner_scope,owner_id,list_type,slot_index,item_core,avatar_uid,"
            + "avatar_expire_date,avatar_clear_id,avatar_jewel_socket,avatar_color1,"
            + "avatar_color2,avatar_delete_date) "
            + "VALUES('character',@cid,@list,@slot,@core,@avatarUid,@expire,@clear,@socket,"
            + "@color1,@color2,@delete) "
            + "ON CONFLICT(avatar_uid) WHERE avatar_uid IS NOT NULL DO NOTHING",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@list", listType);
                parameters.AddWithValue("@slot", slotIndex);
                parameters.AddWithValue("@core", InventoryItemCore.Encode(core));
                BindAvatar(parameters, core, avatarDetail);
            },
            cancellationToken);

    /// <summary>The avatar facts of the row being read, or nothing when it is not an avatar.</summary>
    internal static AvatarDetailRow? Avatar(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : new AvatarDetailRow(
                reader.GetInt64(ordinal + 1),
                reader.GetInt64(ordinal + 2),
                (byte[])reader.GetValue(ordinal + 3),
                reader.GetInt64(ordinal + 4),
                reader.GetInt64(ordinal + 5),
                reader.GetInt64(ordinal + 6));

    /// <summary>The avatar facts of the row being written, or seven nulls when there are none.</summary>
    /// <remarks>
    /// The UID is the one the core already carries, repeated as a column of its own so that it can
    /// be indexed -- a value inside a BLOB cannot be. The two must agree, which is why it is taken
    /// from the core here rather than from the caller.
    /// </remarks>
    private static void BindAvatar(
        SqliteParameterCollection parameters,
        InventoryItemCoreState core,
        InventoryAvatarDetailState? detail)
    {
        parameters.AddWithValue("@avatarUid", detail is null ? DBNull.Value : core.Value);
        parameters.AddWithValue("@expire", detail is null ? DBNull.Value : detail.ExpireDate);
        parameters.AddWithValue("@clear", detail is null ? DBNull.Value : detail.ClearAvatarId);
        parameters.AddWithValue("@socket", detail is null ? DBNull.Value : detail.JewelSocket);
        parameters.AddWithValue("@color1", detail is null ? DBNull.Value : detail.Color1);
        parameters.AddWithValue("@color2", detail is null ? DBNull.Value : detail.Color2);
        parameters.AddWithValue("@delete", detail is null ? DBNull.Value : detail.DeleteDate);
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

/// <summary>The rows of <c>items</c> an account keeps in its cargo.</summary>
/// <remarks>
/// No avatar columns are read. An avatar in the cargo has never carried them -- the facts belonged
/// to a character, and this row belongs to an account -- and the load depends on that: a character's
/// avatar without them is an error, and one of these without them is ordinary.
/// </remarks>
internal sealed class SqliteAccountCargoItemTable(SqliteDatabase database) : IAccountCargoItemTable
{
    public ValueTask<IReadOnlyList<AccountCargoItemRow>> ReadByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT item_uid, owner_id, cargo_character_id, list_type, slot_index, item_core
            FROM items
            WHERE owner_scope = 'account' AND owner_id = @accountId
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

/// <summary>The rows of <c>items</c> that sit in a character's title book.</summary>
/// <remarks>
/// A title book is a container like any other, and its five categories are five container types --
/// 19 through 23. The category the caller works in is that number less nineteen, and it is
/// translated here rather than upstream so that nothing outside this table has to know which
/// numbers the containers were given.
/// </remarks>
internal sealed class SqliteCharacterTitleBookTable(SqliteDatabase database)
    : ICharacterTitleBookTable
{
    /// <summary>The container type the first title-book category is stored as.</summary>
    private const int FirstTitleBookContainer = 19;

    public ValueTask<IReadOnlyList<TitleBookRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT list_type, slot_index, item_core FROM items "
            + "WHERE owner_scope='character' AND owner_id=@cid AND list_type BETWEEN 19 AND 23 "
            + "ORDER BY list_type, slot_index",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new TitleBookRow(
                // Handed up as read. The category narrows to a byte only after it has been found
                // to fit, so an out-of-range one reaches the refusal that names it rather than
                // failing as a conversion on the way there.
                reader.GetInt32(0) - FirstTitleBookContainer,
                reader.GetInt32(1),
                EntryBlob.Bounded(reader, 2, 82, "title book item core", false)),
            cancellationToken);
}
