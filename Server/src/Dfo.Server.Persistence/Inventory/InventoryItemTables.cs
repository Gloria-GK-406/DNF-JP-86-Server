using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

/// <summary>A character's things, and the positions of its own that hold them.</summary>
/// <remarks>
/// Every read here starts from <c>item_slots</c> and joins the thing on, because a position is
/// what says a thing is this character's -- <c>items</c> says only what a thing is. The join is on
/// <c>core_uid</c> rather than on <c>id</c>, so a pile is found by the same statement as anything
/// else: <c>id</c> names the template a pile is a pile of, while <c>core_uid</c> names the row its
/// bytes are on, whichever kind the position holds.
///
/// Three reads rather than one, because three callers want three different orders and the order is
/// part of what each of them means: the entry load reads every list by container and slot, the
/// client's drawing order is slot then id within the main list, and a refill walks the main list by
/// slot alone. Collapsing them into one read and sorting afterwards would move a decision about
/// what the client sees into code that is only about rows.
///
/// A character's inventory stops short of the title-book containers. Their positions are rows of
/// <c>item_slots</c> like any other, but the load they feed parses a container type that admits
/// only 0, 1, 2, 3 and 7, so one reaching it would be refused rather than ignored.
/// </remarks>
internal sealed class SqliteCharacterItemTable(SqliteDatabase database) : ICharacterItemTable
{
    /// <summary>What the load reads, in the order it reads it.</summary>
    /// <remarks>
    /// The avatar columns come with the row now, and there are six of them rather than seven:
    /// whether a row has them is decided by the first of the six, which is written on exactly the
    /// rows that are avatars. The row keeps no key of its own for that any more.
    /// </remarks>
    public ValueTask<IReadOnlyList<CharacterItemRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT held.item_uid, place.owner_scope, place.owner_id, place.list_type,
                   place.slot_index, held.item_core,
                   held.avatar_expire_date, held.avatar_clear_id,
                   held.avatar_jewel_socket, held.avatar_color1, held.avatar_color2,
                   held.avatar_delete_date
            FROM item_slots place
            JOIN items held ON held.item_uid = place.core_uid
            WHERE place.owner_scope = 'character' AND place.owner_id = @characterId
              AND place.list_type IN (0, 1, 2, 3, 7)
            ORDER BY place.list_type, place.slot_index, held.item_uid;
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

    /// <summary>The container a character's worn pieces sit in.</summary>
    private const int EquipmentListType = (int)InventoryContainerType.Equipment;

    public ValueTask<IReadOnlyList<WornItemRow>> ReadWornByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            $"""
            SELECT roster.character_id, held.item_uid, place.slot_index, held.item_core, held.appearance_item_id
            FROM characters roster
            JOIN item_slots place
              ON place.owner_scope = 'character' AND place.owner_id = roster.character_id
            JOIN items held ON held.item_uid = place.core_uid
            WHERE roster.account_id = @accountId AND roster.delete_flag = 0
              AND place.list_type = {EquipmentListType}
            ORDER BY roster.character_id, place.slot_index, held.item_uid;
            """,
            parameters => parameters.AddWithValue("@accountId", accountId.Value),
            reader => new WornItemRow(
                reader.GetInt32(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                InventoryItemCore.Decode(EntryCore(reader, 3, reader.GetInt64(1))) with { AppearanceItemId = reader.IsDBNull(4) ? null : reader.GetInt32(4) }),
            cancellationToken);

    internal static AvatarDetailRow? Avatar(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : new AvatarDetailRow(
                reader.GetInt64(ordinal),
                reader.GetInt64(ordinal + 1),
                (byte[])reader.GetValue(ordinal + 2),
                reader.GetInt64(ordinal + 3),
                reader.GetInt64(ordinal + 4),
                reader.GetInt64(ordinal + 5));

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
            SELECT held.item_uid, place.owner_id,
                   place.list_type, place.slot_index, held.item_core
            FROM item_slots place
            JOIN items held ON held.item_uid = place.core_uid
            WHERE place.owner_scope = 'account' AND place.owner_id = @accountId
              AND place.list_type = 12
            ORDER BY place.list_type, place.slot_index, held.item_uid;
            """,
            parameters => parameters.AddWithValue("@accountId", accountId.Value),
            reader => new AccountCargoItemRow(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                InventoryItemCore.Decode(
                    SqliteCharacterItemTable.EntryCore(reader, 4, reader.GetInt64(0)))),
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
            "SELECT place.list_type, place.slot_index, held.item_core FROM item_slots place "
            + "JOIN items held ON held.item_uid=place.core_uid "
            + "WHERE place.owner_scope='character' AND place.owner_id=@cid "
            + "AND place.list_type BETWEEN 19 AND 23 "
            + "ORDER BY place.list_type, place.slot_index",
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
