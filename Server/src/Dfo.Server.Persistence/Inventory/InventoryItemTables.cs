using System.Buffers.Binary;
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
    /// <summary>Whether some row already carries the avatar a write names, as a condition.</summary>
    /// <remarks>
    /// Two questions in one, and neither of them needs a key column on the row. Whether the row
    /// carries avatar facts at all is asked of the first of the six, exactly as <see cref="Avatar"/>
    /// asks it. Which avatar those facts are for is read out of the core, where the UID has always
    /// been: <see cref="InventoryItemCore"/> writes it as four little-endian bytes at offset five,
    /// which is where SQLite counting a BLOB from one finds bytes six through nine.
    ///
    /// The value it is compared against is bound as those same four bytes, so the comparison is
    /// between two BLOBs and nothing has to unpack one. A write with no avatar facts binds null,
    /// and a comparison with null is never true, so such a write meets no condition at all --
    /// which is what the partial index this replaces did by leaving those rows out.
    /// </remarks>
    internal const string CarriesTheAvatar =
        "held.avatar_expire_date IS NOT NULL AND substr(held.item_core, 6, 4) = @avatarKey";

    /// <summary>The avatar UID inside a core, as the four bytes the column holds it as.</summary>
    internal static byte[] AvatarKey(InventoryItemCoreState core)
    {
        ArgumentNullException.ThrowIfNull(core);
        var key = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(key, core.Value);
        return key;
    }

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
              AND place.list_type NOT BETWEEN 19 AND 23
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
            SELECT roster.character_id, held.item_uid, place.slot_index, held.item_core
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
                InventoryItemCore.Decode(EntryCore(reader, 3, reader.GetInt64(1)))),
            cancellationToken);

    public ValueTask<IReadOnlyList<MainItemRow>> ReadMainAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT held.item_uid,place.slot_index,held.item_core FROM item_slots place "
            + "JOIN items held ON held.item_uid=place.core_uid "
            + "WHERE place.owner_scope='character' AND place.owner_id=@cid "
            + "AND place.list_type=0 "
            + "ORDER BY place.slot_index,held.item_uid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new MainItemRow(
                reader.GetInt64(0),
                reader.GetInt32(1),
                InventoryItemCore.Decode(
                    PackedCore(reader, 2, "Inventory item core is malformed."))),
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
            SELECT held.item_uid, place.owner_id, held.cargo_character_id,
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

/// <summary>The rows of <c>items</c>, read and written as things rather than as places.</summary>
/// <remarks>
/// Beside <see cref="SqliteCharacterItemTable"/>, not instead of it. That one answers a whole
/// inventory in the order the entry load reads it; this one answers one thing by its identity,
/// which is the question the table could not be asked while a row was destroyed and rebuilt every
/// time the thing moved.
///
/// Official mail creates a detached ordinary equipment row here, then gives its custody to the
/// mailbox in the same transaction. Claiming transfers that existing identity into a container.
/// </remarks>
internal sealed class SqliteWorldItemTable(SqliteDatabase database) : IWorldItemTable
{
    /// <summary>What the database calls the bound on an identity it may allocate.</summary>
    private const string WorldIdentityConstraint = "world_identity_fits_the_wire";

    /// <summary>The one row the insert answers with, so the write path can return it.</summary>
    private sealed record Allocated(long ItemUid);

    /// <remarks>
    /// The identity is allocated by the column and read back rather than chosen here, so there is
    /// no moment between deciding one and using it in which somebody else could take the same one.
    ///
    /// The database refuses to allocate one that would not fit the wire, and that refusal arrives
    /// as a constraint failure naming the constraint. It is turned into a sentence here because
    /// "CHECK constraint failed" says nothing about what has actually happened, which is that this
    /// database has handed out every identity it is able to say.
    /// </remarks>
    public async ValueTask<int> InsertAsync(
        InventoryItemCoreState core,
        AvatarDetailRow? avatar,
        CreatureDetailRow? creature,
        CancellationToken cancellationToken)
    {
        Allocated? allocated;
        try
        {
            allocated = await database.WriteReturningAsync(
                """
                INSERT INTO items(
                    item_core,
                    avatar_expire_date, avatar_clear_id, avatar_jewel_socket,
                    avatar_color1, avatar_color2, avatar_delete_date,
                    creature_field04, creature_mode_flag, creature_progress_value,
                    creature_mode1_field0a, creature_mode1_field0b, creature_field_after_value,
                    creature_text, creature_tail_flag)
                VALUES(
                    @core,
                    @expire, @clear, @socket, @color1, @color2, @delete,
                    @field04, @modeFlag, @progress, @mode1a, @mode1b, @afterValue,
                    @creatureText, @tailFlag)
                RETURNING item_uid;
                """,
                parameters =>
                {
                    parameters.AddWithValue("@core", InventoryItemCore.Encode(core));
                    BindAvatarColumns(parameters, avatar);
                    BindCreatureColumns(parameters, creature);
                },
                reader => new Allocated(reader.GetInt64(0)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException failure) when (
            failure.Message.Contains(WorldIdentityConstraint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "This database has no world identity left to give: the next one would not fit the "
                + "signed 32-bit field a creature's identity goes on the wire as.",
                failure);
        }

        return allocated is null
            ? throw new InvalidOperationException("The world took the item and named no identity.")
            : checked((int)allocated.ItemUid);
    }

    public ValueTask<WorldItemRow?> FindAsync(int itemUid, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT item_uid, item_core,
                   avatar_expire_date, avatar_clear_id, avatar_jewel_socket,
                   avatar_color1, avatar_color2, avatar_delete_date,
                   creature_field04, creature_mode_flag, creature_progress_value,
                   creature_mode1_field0a, creature_mode1_field0b, creature_field_after_value,
                   creature_text, creature_tail_flag
            FROM items
            WHERE item_uid = @uid;
            """,
            parameters => parameters.AddWithValue("@uid", itemUid),
            Read,
            cancellationToken);

    /// <remarks>
    /// One condition, and it is the whole question: does any position name this row.
    ///
    /// It used to take two, and the second was the difficulty. A pile keeps a row of <c>items</c>
    /// too, and the position holding a pile names the template it is a pile of rather than that
    /// row -- so no slot's <c>id</c> ever named a pile's row, and asking only "does a slot name
    /// this row" reported every pile in the database. Telling the two apart then needed the place
    /// the row was standing in, which meant the position columns <c>items</c> carried.
    ///
    /// A position now names its row outright, whichever kind it holds: <c>core_uid</c> is the row
    /// the eighty-two bytes are on, and it is written for a pile exactly as it is for a thing with
    /// an identity. So what says a row is held is the same fact for both, the second condition is
    /// gone, and nothing here reads a position off <c>items</c> -- which no longer has one.
    /// </remarks>
    public ValueTask<IReadOnlyList<WorldItemRow>> ReadUnreferencedAsync(
        CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT item_uid, item_core,
                   avatar_expire_date, avatar_clear_id, avatar_jewel_socket,
                   avatar_color1, avatar_color2, avatar_delete_date,
                   creature_field04, creature_mode_flag, creature_progress_value,
                   creature_mode1_field0a, creature_mode1_field0b, creature_field_after_value,
                   creature_text, creature_tail_flag
            FROM items
            WHERE NOT EXISTS (
                      SELECT 1 FROM item_slots holder
                      WHERE holder.core_uid = items.item_uid)
            ORDER BY item_uid;
            """,
            _ => { },
            Read,
            cancellationToken);

    /// <summary>One thing in the world, from the sixteen columns both reads above ask for.</summary>
    private static WorldItemRow Read(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        InventoryItemCore.Decode(
            SqliteCharacterItemTable.EntryCore(reader, 1, reader.GetInt64(0))),
        SqliteCharacterItemTable.Avatar(reader, 2),
        Creature(reader, 8));

    /// <remarks>
    /// The identity is not among the columns written. It is what the row is, and a write that
    /// could change it would be the very thing this whole shape exists to remove.
    /// </remarks>
    public ValueTask<int> UpdateAsync(
        int itemUid,
        InventoryItemCoreState core,
        AvatarDetailRow? avatar,
        CreatureDetailRow? creature,
        CancellationToken cancellationToken) =>
        database.ExecuteAsync(
            """
            UPDATE items SET item_core=@core,
                avatar_expire_date=@expire, avatar_clear_id=@clear,
                avatar_jewel_socket=@socket, avatar_color1=@color1, avatar_color2=@color2,
                avatar_delete_date=@delete,
                creature_field04=@field04, creature_mode_flag=@modeFlag,
                creature_progress_value=@progress, creature_mode1_field0a=@mode1a,
                creature_mode1_field0b=@mode1b, creature_field_after_value=@afterValue,
                creature_text=@creatureText, creature_tail_flag=@tailFlag,
                updated_at=CURRENT_TIMESTAMP
            WHERE item_uid=@uid
            """,
            parameters =>
            {
                parameters.AddWithValue("@core", InventoryItemCore.Encode(core));
                BindAvatarColumns(parameters, avatar);
                BindCreatureColumns(parameters, creature);
                parameters.AddWithValue("@uid", itemUid);
            },
            cancellationToken);

    public ValueTask<int> DeleteAsync(int itemUid, CancellationToken cancellationToken) =>
        database.ExecuteAsync(
            "DELETE FROM items WHERE item_uid=@uid",
            parameters => parameters.AddWithValue("@uid", itemUid),
            cancellationToken);

    /// <summary>The creature facts of the row being read, or nothing when it is not a creature.</summary>
    /// <remarks>
    /// Whether a row has them is decided by the first of the eight, which is set on exactly the
    /// rows that are creatures -- the same way an avatar's presence is decided by the first of
    /// its six.
    /// </remarks>
    private static CreatureDetailRow? Creature(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : new CreatureDetailRow(
                reader.GetInt32(ordinal),
                reader.GetInt32(ordinal + 1),
                reader.GetInt32(ordinal + 2),
                reader.GetInt32(ordinal + 3),
                reader.GetInt32(ordinal + 4),
                reader.GetInt32(ordinal + 5),
                EntryBlob.Bounded(
                    reader, ordinal + 6, EntryBlob.CreatureText, "creature text", true),
                reader.GetInt32(ordinal + 7));

    /// <summary>The avatar facts of the row being written, or six nulls when there are none.</summary>
    /// <remarks>
    /// The avatar's own UID is not among them, exactly as the per-slot write no longer sends one:
    /// it is inside the core, and the row does not repeat it.
    /// </remarks>
    private static void BindAvatarColumns(
        SqliteParameterCollection parameters,
        AvatarDetailRow? avatar)
    {
        parameters.AddWithValue("@expire", avatar is null ? DBNull.Value : avatar.ExpireDate);
        parameters.AddWithValue("@clear", avatar is null ? DBNull.Value : avatar.ClearAvatarId);
        parameters.AddWithValue("@socket", avatar is null ? DBNull.Value : avatar.JewelSocket);
        parameters.AddWithValue("@color1", avatar is null ? DBNull.Value : avatar.Color1);
        parameters.AddWithValue("@color2", avatar is null ? DBNull.Value : avatar.Color2);
        parameters.AddWithValue("@delete", avatar is null ? DBNull.Value : avatar.DeleteDate);
    }

    /// <summary>The creature facts of the row being written, or eight nulls when there are none.</summary>
    private static void BindCreatureColumns(
        SqliteParameterCollection parameters, CreatureDetailRow? creature)
    {
        parameters.AddWithValue("@field04", creature is null ? DBNull.Value : creature.Field04);
        parameters.AddWithValue("@modeFlag", creature is null ? DBNull.Value : creature.ModeFlag);
        parameters.AddWithValue(
            "@progress", creature is null ? DBNull.Value : creature.ProgressValue);
        parameters.AddWithValue("@mode1a", creature is null ? DBNull.Value : creature.Mode1Field0A);
        parameters.AddWithValue("@mode1b", creature is null ? DBNull.Value : creature.Mode1Field0B);
        parameters.AddWithValue(
            "@afterValue", creature is null ? DBNull.Value : creature.FieldAfterValue);
        parameters.AddWithValue(
            "@creatureText", creature is null ? DBNull.Value : creature.CreatureText);
        parameters.AddWithValue("@tailFlag", creature is null ? DBNull.Value : creature.TailFlag);
    }
}

/// <summary>The rows of <c>item_slots</c>: what each position holds.</summary>
/// <remarks>
/// One statement shape for both kinds of content. What a slot holds is three columns -- which kind
/// of thing its id names, the id, and how many -- and a slot holding a sword differs from one
/// holding a stack of potions only in what those three say. Nothing here branches on the kind,
/// which is the point: the branch is the reader's, on a column that says outright which of the two
/// the id is, rather than a rule about item kinds that somebody has to remember.
///
/// Nothing in the server reaches this yet.
/// </remarks>
internal sealed class SqliteItemSlotTable(SqliteDatabase database) : IItemSlotTable
{
    public ValueTask<IReadOnlyList<ItemSlotRow>> ReadByOwnerAsync(
        string ownerScope, int ownerId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT owner_scope, owner_id, list_type, slot_index, kind, id, core_uid, count
            FROM item_slots
            WHERE owner_scope = @scope AND owner_id = @owner
            ORDER BY list_type, slot_index;
            """,
            parameters =>
            {
                parameters.AddWithValue("@scope", ownerScope);
                parameters.AddWithValue("@owner", ownerId);
            },
            Read,
            cancellationToken);

    public ValueTask<ItemSlotRow?> FindAsync(ItemPlace place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        return database.ReadFirstAsync(
            """
            SELECT owner_scope, owner_id, list_type, slot_index, kind, id, core_uid, count
            FROM item_slots
            WHERE owner_scope = @scope AND owner_id = @owner
              AND list_type = @list AND slot_index = @slot;
            """,
            parameters => Bind(parameters, place),
            Read,
            cancellationToken);
    }

    /// <remarks>
    /// A taken position is left alone rather than overwritten. A slot that replaced what it held
    /// would drop a world row's only reference with nothing anywhere saying it had happened, and
    /// the caller reads the untouched count as the refusal it is.
    /// </remarks>
    public ValueTask<int> InsertAsync(ItemSlotRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        return database.ExecuteAsync(
            "INSERT INTO item_slots"
            + "(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) "
            + "VALUES(@scope,@owner,@list,@slot,@kind,@id,@core,@count) "
            + "ON CONFLICT(owner_scope,owner_id,list_type,slot_index) DO NOTHING",
            parameters =>
            {
                Bind(parameters, row.Place);
                parameters.AddWithValue("@kind", Name(row.Kind));
                parameters.AddWithValue("@id", row.Id);
                parameters.AddWithValue("@core", row.CoreUid);
                parameters.AddWithValue("@count", row.Count);
            },
            cancellationToken);
    }

    public ValueTask<int> ClearAsync(ItemPlace place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        return database.ExecuteAsync(
            "DELETE FROM item_slots WHERE owner_scope=@scope AND owner_id=@owner "
            + "AND list_type=@list AND slot_index=@slot",
            parameters => Bind(parameters, place),
            cancellationToken);
    }

    private static ItemSlotRow Read(SqliteDataReader reader) => new(
        new ItemPlace(
            reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
        Kind(reader.GetString(4)),
        reader.GetInt32(5),
        reader.GetInt32(6),
        reader.GetInt32(7));

    private static void Bind(SqliteParameterCollection parameters, ItemPlace place)
    {
        parameters.AddWithValue("@scope", place.OwnerScope);
        parameters.AddWithValue("@owner", place.OwnerId);
        parameters.AddWithValue("@list", place.ListType);
        parameters.AddWithValue("@slot", place.SlotIndex);
    }

    /// <summary>What the column calls that kind.</summary>
    internal static string Name(ItemSlotKind kind) => kind switch
    {
        ItemSlotKind.World => "world",
        ItemSlotKind.Template => "template",
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "A slot holds a world row or a template, and nothing else."),
    };

    /// <summary>Which kind the column is saying, refusing what the check should have stopped.</summary>
    private static ItemSlotKind Kind(string stored) => stored switch
    {
        "world" => ItemSlotKind.World,
        "template" => ItemSlotKind.Template,
        _ => throw new InvalidDataException($"A slot says it holds '{stored}', which is nothing."),
    };
}
