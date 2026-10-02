using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
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
internal sealed class SqliteItemDomainStore : IItemDomainStore
{
    /// <summary>What the database registers this store's session under.</summary>
    private static readonly object FacetKey = typeof(SqliteItemDomainStoreSession);

    private readonly string connectionString;

    public SqliteItemDomainStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        this.connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
    }

    public object StoreIdentity => this.connectionString;

    public bool IsDurable => true;

    public IItemDomainStoreSession SessionIn(IDomainTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var database = transaction.Enlist(
            this.StoreIdentity,
            () => new SqliteDatabaseParticipant(this.connectionString));
        return database.Facet(FacetKey, owner => new SqliteItemDomainStoreSession(owner));
    }
}

/// <summary>One use case's reads and writes against the items in the character database.</summary>
/// <remarks>
/// Writes are held here until the database prepares, so that everything the use case decided
/// reaches it as one statement batch inside one database transaction. Reads consult the held
/// writes first, which is what lets the use case read back what it has just decided without
/// anyone outside seeing it.
///
/// <b>Settlement writes two tables.</b> A thing with an identity is a row of <c>items</c> -- the
/// world side -- and a position that holds it is a row of <c>item_slots</c> saying which world row
/// it holds. A pile has no identity to keep, so its position says which template it holds and how
/// many, and its <c>items</c> row is the disposable one it has always been.
///
/// <b>A move does not destroy the thing that moved.</b> The batch below empties every position it
/// names before it fills any, exactly as it always has; what is new is that emptying a position no
/// longer reaches the thing standing in it when that thing survives the batch. It stays exactly
/// where it was -- which is nowhere, because a thing is not anywhere -- keeping its identity, its
/// creation time and every column hanging off it, and a position further down says it holds it
/// again. Only the slot rows are destroyed and rebuilt, and a slot row carries nothing but where
/// and what, so rebuilding one loses nothing.
///
/// <b>Nothing is parked anywhere for the length of the batch.</b> An earlier shape of this had to
/// lift a surviving row out of its position first, because <c>items</c> held its four position
/// columns unique and a fill could otherwise meet the row it was replacing. <c>items</c> holds no
/// position at all now, so there is no uniqueness to dodge and no sentinel container to dodge it
/// with: what a position holds is decided entirely by which slot rows this batch leaves behind.
/// </remarks>
internal sealed class SqliteItemDomainStoreSession(SqliteDatabaseParticipant owner)
    : IItemDomainStoreSession, ISqliteStoreFacet
{
    internal static SqliteItemDomainStoreSession? ExistingIn(SqliteDatabaseParticipant owner) =>
        owner.ExistingFacet<SqliteItemDomainStoreSession>(typeof(SqliteItemDomainStoreSession));
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
                SELECT place.slot_index, held.item_core, held.item_uid
                FROM item_slots place
                JOIN items held ON held.item_uid = place.core_uid
                WHERE place.owner_scope = @scope AND place.owner_id = @owner
                  AND place.list_type = @list;
                """;
            command.Parameters.AddWithValue("@scope", place.Scope);
            command.Parameters.AddWithValue("@owner", place.Owner);
            command.Parameters.AddWithValue("@list", place.ListType);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var slot = reader.GetInt32(0);
                items[slot] = new StoredItem(slot, DecodeCore(reader, 1))
                {
                    Identity = new ItemIdentity(reader.GetInt32(2)),
                };
            }
        }

        if (this.staged.TryGetValue(containerId, out var slots))
        {
            var identities = await this.StagedIdentitiesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var (slot, item) in slots)
            {
                if (item is null)
                {
                    items.Remove(slot);
                }
                else
                {
                    items[slot] = item with { Identity = identities.GetValueOrDefault((containerId, slot)) };
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
            if (held is null) return null;
            var identities = await this.StagedIdentitiesAsync(cancellationToken).ConfigureAwait(false);
            return held with { Identity = identities.GetValueOrDefault((containerId, slotIndex)) };
        }

        var place = OwnerOf(containerId);
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            SELECT held.item_core, held.item_uid
            FROM item_slots place
            JOIN items held ON held.item_uid = place.core_uid
            WHERE place.owner_scope = @scope AND place.owner_id = @owner
              AND place.list_type = @list AND place.slot_index = @slot;
            """;
        command.Parameters.AddWithValue("@scope", place.Scope);
        command.Parameters.AddWithValue("@owner", place.Owner);
        command.Parameters.AddWithValue("@list", place.ListType);
        command.Parameters.AddWithValue("@slot", slotIndex);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredItem(slotIndex, DecodeCore(reader, 0))
            {
                Identity = new ItemIdentity(reader.GetInt32(1)),
            }
            : null;
    }

    /// <remarks>
    /// The class the write carries is required rather than optional. Settlement decides on it
    /// whether the thing being written keeps an identity of its own or is a pile counted in its
    /// slot, and there is no second reading of the row it could fall back on: the kind byte a core
    /// carries is put there by the caller and the archive's answer is not, which is the whole
    /// reason the classification exists. A write that did not say would therefore have to be
    /// guessed at, and a guess of "pile" silently throws away the identity of everything it is
    /// wrong about.
    /// </remarks>
    public void Write(ContainerId containerId, StoredItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _ = OwnerOf(containerId);
        ValidateSlot(item.SlotIndex);
        if (item.WorldClass is null)
        {
            throw new ArgumentException(
                "A write says what the archive makes of the thing being written, because "
                + "settlement decides between a world row and a pile on it and cannot read that "
                + "off the row.",
                nameof(item));
        }

        var slots = this.Staged(containerId);
        if (item.TransferFrom is null
            && slots.TryGetValue(item.SlotIndex, out var prior)
            && prior?.TransferFrom is { } origin
            && item.WorldClass is not ItemWorldClass.Interchangeable
            && item.Core.ItemKind == prior.Core.ItemKind
            && item.Core.ItemId == prior.Core.ItemId
            && item.Core.Value == prior.Core.Value)
            item = item with { TransferFrom = origin };
        slots[item.SlotIndex] = item;
    }

    private async ValueTask<Dictionary<(ContainerId, int), ItemIdentity>> StagedIdentitiesAsync(
        CancellationToken cancellationToken)
    {
        // Resolve exactly the rows settlement will preserve, without flushing or allocating new UIDs.
        var survivors = Survivors(this.Placings(),
            await this.DisplacedAsync(cancellationToken, includeTransferSources: true).ConfigureAwait(false));
        return survivors.ToDictionary(pair => pair.Key,
            pair => new ItemIdentity(checked((int)pair.Value.ItemUid)));
    }

    public void Clear(ContainerId containerId, int slotIndex)
    {
        _ = OwnerOf(containerId);
        ValidateSlot(slotIndex);
        this.Staged(containerId)[slotIndex] = null;
    }

    /// <remarks>
    /// The staged writes are let go of once applied, so that preparing finds nothing left to do
    /// rather than replacing every one of those rows a second time. Replaying them would not
    /// corrupt anything -- a batch clears every slot it names before filling any -- but it would
    /// give each row yet another surrogate key for no reason.
    /// </remarks>
    public async ValueTask ApplyDecidedWritesAsync(CancellationToken cancellationToken)
    {
        if (!this.HasStagedWork)
        {
            return;
        }

        await this.FlushAsync(cancellationToken).ConfigureAwait(false);
        this.staged.Clear();
    }

    /// <remarks>
    /// Three passes over the positions this batch names, in this order and for these reasons.
    ///
    /// The rows about to be displaced are read first, because everything after this changes them.
    /// Then every named position is emptied before any is filled, which is what a swap needs and
    /// what this has always done -- but emptying one no longer reaches the thing standing in it
    /// when that thing is one this batch is putting down again. The survivors are known by then,
    /// so the delete simply spares them; nothing has to be moved out of the way first.
    ///
    /// What an avatar carries is read before the first delete, and only for the avatars whose rows
    /// are not among the survivors. A survivor keeps its own columns by never being deleted; it is
    /// the one whose row is being replaced that has to be handed its facts back.
    /// </remarks>
    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        var displaced = await this.DisplacedAsync(cancellationToken).ConfigureAwait(false);
        var survivors = Survivors(this.Placings(), displaced);
        var carried = await this.CarriedAvatarsAsync(survivors, cancellationToken)
            .ConfigureAwait(false);
        var spared = survivors.Values.Select(survivor => survivor.ItemUid).Distinct().ToArray();

        foreach (var (containerId, slots) in this.staged)
        {
            var place = OwnerOf(containerId);
            foreach (var slot in slots.Keys)
            {
                await this.EmptyAsync(place, slot, spared, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var (containerId, slots) in this.staged)
        {
            var place = OwnerOf(containerId);
            foreach (var (slot, item) in slots)
            {
                if (item is null)
                {
                    continue;
                }

                var identity = survivors.TryGetValue((containerId, slot), out var survivor)
                    ? await this.LandAsync(item, survivor, cancellationToken).ConfigureAwait(false)
                    : await this.FillAsync(item, carried, cancellationToken).ConfigureAwait(false);
                await this.HoldAsync(place, slot, item, identity, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>One row this batch is about to displace, as it stands before the batch runs.</summary>
    private sealed record Displaced(
        ContainerId ContainerId, int SlotIndex, long ItemUid, byte[] Core);

    /// <summary>One thing this batch is putting somewhere, and what the archive makes of it.</summary>
    private sealed record Placing(
        ContainerId ContainerId, int SlotIndex, StoredItem Item, byte[] Core);

    /// <summary>What this batch is putting somewhere, in an order that does not vary.</summary>
    /// <remarks>
    /// Dictionary enumeration order is not part of the dictionary's contract, and which of two
    /// identical rows ends up carrying which identity would otherwise be decided by it.
    /// </remarks>
    private List<Placing> Placings() =>
    [
        .. this.staged
            .SelectMany(entry => entry.Value
                .Where(slot => slot.Value is not null)
                .Select(slot => new Placing(
                    entry.Key,
                    slot.Key,
                    slot.Value!,
                    InventoryItemCore.Encode(slot.Value!.Core))))
            .OrderBy(placing => placing.ContainerId.Value, StringComparer.Ordinal)
            .ThenBy(placing => placing.SlotIndex),
    ];

    /// <summary>The rows standing in the positions this batch names, before it touches them.</summary>
    private async ValueTask<List<Displaced>> DisplacedAsync(CancellationToken cancellationToken,
        bool includeTransferSources = false)
    {
        var displaced = new List<Displaced>();
        var positions = this.staged.SelectMany(entry => entry.Value.Keys
            .Select(slot => new ItemTransferSource(entry.Key, slot)));
        if (includeTransferSources)
            positions = positions.Concat(this.Placings()
                .Where(placing => placing.Item.TransferFrom is not null)
                .Select(placing => placing.Item.TransferFrom!.Value));
        foreach (var group in positions.Distinct().GroupBy(position => position.Container)
                     .OrderBy(group => group.Key.Value, StringComparer.Ordinal))
        {
            var containerId = group.Key;
            var place = OwnerOf(containerId);
            foreach (var slot in group.Select(position => position.SlotIndex).Order())
            {
                await using var command = await owner.CommandAsync(cancellationToken)
                    .ConfigureAwait(false);
                command.CommandText = """
                    SELECT held.item_uid, held.item_core
                    FROM item_slots place
                    JOIN items held ON held.item_uid = place.core_uid
                    WHERE place.owner_scope = @scope AND place.owner_id = @owner
                      AND place.list_type = @list AND place.slot_index = @slot;
                    """;
                command.Parameters.AddWithValue("@scope", place.Scope);
                command.Parameters.AddWithValue("@owner", place.Owner);
                command.Parameters.AddWithValue("@list", place.ListType);
                command.Parameters.AddWithValue("@slot", slot);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    displaced.Add(new Displaced(
                        containerId, slot, reader.GetInt64(0), (byte[])reader.GetValue(1)));
                }
            }
        }

        return displaced;
    }

    /// <summary>
    /// Which row already in this batch's way is the same thing as something it is putting down.
    /// </summary>
    /// <remarks>
    /// A pile is never one of these. Two of it are the same thing, so there is nothing about the
    /// row it sat on worth keeping and it goes on being written the way it always was.
    ///
    /// Three passes, narrowest first, because the first two decide the cases that matter and the
    /// third must not be allowed to answer one of them. A thing put back where it was is itself; a
    /// thing that appears elsewhere carrying exactly what some displaced row carried is that row
    /// having moved; and only then, a position rewritten with the same template, kind and value is
    /// the same thing with something about it changed. Letting the third pass go first would make
    /// two swapped pieces of one template trade identities.
    /// </remarks>
    private static Dictionary<(ContainerId, int), Displaced> Survivors(
        List<Placing> placings, List<Displaced> displaced)
    {
        var survivors = new Dictionary<(ContainerId, int), Displaced>();
        var taken = new HashSet<long>();
        var open = placings
            .Where(placing => placing.Item.WorldClass is not ItemWorldClass.Interchangeable)
            .ToList();

        foreach (var placing in open.Where(placing => placing.Item.TransferFrom is not null).ToArray())
        {
            var from = placing.Item.TransferFrom!.Value;
            var row = displaced.SingleOrDefault(candidate =>
                candidate.ContainerId == from.Container && candidate.SlotIndex == from.SlotIndex);
            if (row is null || taken.Contains(row.ItemUid) || !SameThing(placing, row))
                throw new InvalidOperationException(
                    $"Transfer source {from.Container.Value}/{from.SlotIndex} does not hold the staged instance.");
            survivors.Add((placing.ContainerId, placing.SlotIndex), row);
            _ = taken.Add(row.ItemUid);
            _ = open.Remove(placing);
        }

        Claim((placing, row) => InPlace(placing, row) && SameContent(placing, row));
        Claim(SameContent);
        Claim((placing, row) => InPlace(placing, row) && SameThing(placing, row));
        return survivors;

        void Claim(Func<Placing, Displaced, bool> matches)
        {
            foreach (var placing in open.ToList())
            {
                var row = displaced.FirstOrDefault(
                    candidate => !taken.Contains(candidate.ItemUid) && matches(placing, candidate));
                if (row is null)
                {
                    continue;
                }

                survivors.Add((placing.ContainerId, placing.SlotIndex), row);
                _ = taken.Add(row.ItemUid);
                _ = open.Remove(placing);
            }
        }

        static bool InPlace(Placing placing, Displaced row) =>
            row.ContainerId == placing.ContainerId && row.SlotIndex == placing.SlotIndex;

        static bool SameContent(Placing placing, Displaced row) =>
            placing.Core.AsSpan().SequenceEqual(row.Core);

        static bool SameThing(Placing placing, Displaced row)
        {
            var core = InventoryItemCore.Decode(row.Core);
            if (core.ItemKind == 5 && placing.Item.Core.ItemKind == 5
                && core.Value == row.ItemUid && placing.Item.Core.Value == row.ItemUid)
                return true;
            return core.ItemId == placing.Item.Core.ItemId
                && core.ItemKind == placing.Item.Core.ItemKind
                && core.Value == placing.Item.Core.Value;
        }
    }

    /// <summary>What each avatar whose row is being replaced already carries, by its UID.</summary>
    /// <remarks>
    /// An avatar is an item of kind 8, and the UID its facts are found under is the value inside
    /// its core. A row being replaced would leave its facts on the row it deleted -- which is to
    /// say, would throw them away -- so they are read here and written on again.
    ///
    /// The row they are read off is found by that same value, read out of its core: the four
    /// little-endian bytes <see cref="InventoryItemCore"/> writes at offset five, which is where
    /// SQLite counting a BLOB from one finds bytes six through nine. Whether that row carries
    /// facts at all is asked of the first of the six, because nothing else says so now -- the row
    /// keeps no separate key for its avatar.
    ///
    /// A surviving row is not asked about. It keeps its own columns because nothing deletes it,
    /// which is the whole of what "its state follows it" means.
    ///
    /// Three things in this file are true today for reasons that live in another method. None of
    /// them is an invariant this class enforces, so each is written down rather than trusted:
    ///
    /// First, this SELECT is not the only thing that decides when the guard in
    /// <see cref="FillAsync"/> engages. That guard runs when the @expire the insert binds is not
    /// null, and @expire has two sources: this retrieval, and the six facts a write carried itself.
    /// What this retrieval contributes is a filter on avatar_expire_date IS NOT NULL, which is what
    /// makes a returned row.s first column never null, which is what makes "this row carries no
    /// facts" and "no row was found" the same question here; loosen this filter and the guard
    /// starts refusing rows the old index let through. A write carrying its own facts makes @expire
    /// non-null with nothing found here at all -- precisely the case the retired index covered and
    /// this retrieval never could reach: an avatar entering the world for the first time, which no
    /// standing row can be asked about. So the guard is not a property of either method, and it is
    /// coupled to what the write carries as well as to this filter.
    ///
    /// Second, <see cref="LandAsync"/> has no avatar-uniqueness guard of its own and does not need
    /// one, because every one of the three passes in Survivors matches on the value inside the
    /// core: a row it lands on is a row already carrying that same UID. Change a pass to match on
    /// anything looser and this method's silence becomes a hole.
    ///
    /// Creature core changes retain the existing world row when its core UID still names that row;
    /// a creature without a surviving row must supply creation facts instead.
    /// </remarks>
    private async ValueTask<IReadOnlyDictionary<int, object?[]>> CarriedAvatarsAsync(
        IReadOnlyDictionary<(ContainerId, int), Displaced> survivors,
        CancellationToken cancellationToken)
    {
        var carried = new Dictionary<int, object?[]>();
        foreach (var uid in this.staged
                     .SelectMany(entry => entry.Value.Select(
                         slot => (Place: (entry.Key, slot.Key), slot.Value)))
                     .Where(staged => staged.Value is { Core.ItemKind: AvatarKind }
                         && !survivors.ContainsKey(staged.Place))
                     .Select(staged => staged.Value!.Core.Value)
                     .Distinct())
        {
            await using var command = await owner.CommandAsync(cancellationToken)
                .ConfigureAwait(false);
            command.CommandText = """
                SELECT avatar_expire_date, avatar_clear_id, avatar_jewel_socket,
                       avatar_color1, avatar_color2, avatar_delete_date
                FROM items
                WHERE avatar_expire_date IS NOT NULL
                  AND substr(item_core, 6, 4) = @uid;
                """;
            command.Parameters.AddWithValue("@uid", AvatarKeyOf(uid));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var facts = new object?[6];
                for (var index = 0; index < facts.Length; index++)
                {
                    facts[index] = reader.IsDBNull(index) ? null : reader.GetValue(index);
                }

                carried[uid] = facts;
            }
        }

        return carried;
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

    /// <summary>Settles what a thing that survived this batch carries.</summary>
    /// <remarks>
    /// Its core is written because the third pass above admits a thing whose payload changed while
    /// it stayed put. Nothing else about the row is touched: not the identity, not the creation
    /// time, and not the avatar or creature columns, which is what makes the state follow the
    /// thing rather than the position.
    ///
    /// Where it ends up is not written here and is not a column of this table. The position that
    /// holds it is a row of its own, put down by the caller a line later.
    /// </remarks>
    private async ValueTask<int> LandAsync(
        StoredItem item,
        Displaced survivor,
        CancellationToken cancellationToken)
    {
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            UPDATE items SET item_core = @core, updated_at = CURRENT_TIMESTAMP
            WHERE item_uid = @uid;
            """;
        command.Parameters.AddWithValue("@core", InventoryItemCore.Encode(item.Core));
        command.Parameters.AddWithValue("@uid", survivor.ItemUid);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return checked((int)survivor.ItemUid);
    }

    /// <summary>Empties one named position, and gets rid of what stood in it unless it stays.</summary>
    /// <remarks>
    /// The position is what says a thing is here, so it is the position that is asked which row to
    /// take away. A row this batch is putting down again is spared by name: it is the one thing an
    /// emptying must not reach, and sparing it here is what replaces moving it out of the way
    /// first.
    ///
    /// The slot row goes either way. It says where and what and nothing else, so destroying and
    /// rebuilding one loses nothing at all -- and every named position being empty before any is
    /// filled is what lets two things swap places without anywhere to park.
    /// </remarks>
    private async ValueTask EmptyAsync(
        (string Scope, int Owner, int ListType) place,
        int slotIndex,
        IReadOnlyList<long> spared,
        CancellationToken cancellationToken)
    {
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        var keep = spared.Count == 0
            ? string.Empty
            : " AND item_uid NOT IN ("
              + string.Join(',', spared.Select((_, index) => $"@keep{index}")) + ")";

        command.CommandText =
            "DELETE FROM items WHERE item_uid IN ("
            + "SELECT core_uid FROM item_slots "
            + "WHERE owner_scope = @scope AND owner_id = @owner "
            + "AND list_type = @list AND slot_index = @slot)"
            + keep + ";"
            + "DELETE FROM item_slots "
            + "WHERE owner_scope = @scope AND owner_id = @owner "
            + "AND list_type = @list AND slot_index = @slot;";
        command.Parameters.AddWithValue("@scope", place.Scope);
        command.Parameters.AddWithValue("@owner", place.Owner);
        command.Parameters.AddWithValue("@list", place.ListType);
        command.Parameters.AddWithValue("@slot", slotIndex);
        for (var index = 0; index < spared.Count; index++)
        {
            command.Parameters.AddWithValue($"@keep{index}", spared[index]);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a row for something this batch is putting down for the first time.</summary>
    /// <remarks>
    /// The identity is allocated by the column and read back rather than chosen here, so there is
    /// no moment between deciding one and using it in which somebody else could take the same one.
    /// The database refuses to allocate one that would not fit the wire, and that refusal is turned
    /// into a sentence because "CHECK constraint failed" says nothing about what has happened,
    /// which is that this database has handed out every identity it is able to say.
    /// <para>
    /// The six avatar columns are taken from what the write itself carried whenever it carried
    /// anything, and only otherwise from what the row being replaced already had. The two answer
    /// different questions and neither stands in for the other: a write carrying its own facts is
    /// an avatar entering the world, which no row can be asked about because there is no row yet,
    /// while <see cref="CarriedAvatarsAsync"/> is an avatar the world already holds having its row
    /// rebuilt, which the write has nothing to add to. Preferring the write is what lets an avatar
    /// be put down for the first time with its facts on it; keeping the retrieval is what stops a
    /// move from throwing away the facts of one already standing.
    /// </para>
    /// <para>
    /// <b>The guard in the WHERE clause was, until a write could carry facts, unreachable for
    /// anything this world put down for the first time.</b> Its condition opens with
    /// <c>@expire IS NULL</c>, and @expire had a single source then: the six columns of a standing
    /// row, read back by <see cref="CarriedAvatarsAsync"/>. An avatar entering the world for the
    /// first time has no standing row to be read off, so its insert bound null there, satisfied the
    /// condition on its left side, and never reached the subquery at all. The row it landed carried
    /// six null columns as a result -- and the subquery selects on
    /// <c>standing.avatar_expire_date IS NOT NULL</c>, so the next write under the same identity
    /// could not see that row either. The blindness fed itself: a first-time avatar was neither
    /// judged nor visible to the judgement of the next one, and two of them could stand in one
    /// world under one identity with nothing raised anywhere. What the guard did judge was only the
    /// other case, a standing row being rebuilt, whose facts the retrieval does supply.
    /// </para>
    /// <para>
    /// It engages now because @expire has the second source described above. A write that brings an
    /// avatar in carries its own six, so the insert binds a non-null @expire and the condition falls
    /// through to the subquery; and the row that insert lands carries a non-null
    /// avatar_expire_date, which is what makes it visible to the write after it. One change breaks
    /// both ends of the circle and nothing breaks either end without it, which is why
    /// <c>TwoAvatarsDoNotShareOneIdentityTests</c> holds the contrast between a pair of writes
    /// carrying no facts and the same pair carrying them, rather than the refusal on its own: a
    /// case that only showed a collision refused would be satisfied by a guard that had always
    /// engaged, and this one never had.
    /// </para>
    /// <para>
    /// A refusal is not an error the database raises. The statement succeeds and writes no row, so
    /// <c>RETURNING</c> yields nothing and the scalar read answers null; the sentence thrown below
    /// is what turns that silence into a refusal. It is thrown inside the transaction the
    /// participant holds open at prepare, so the settlement carrying the collision is taken back
    /// whole rather than landing everything except the avatar.
    /// </para>
    /// </remarks>
    private async ValueTask<int> FillAsync(
        StoredItem item,
        IReadOnlyDictionary<int, object?[]> carried,
        CancellationToken cancellationToken)
    {
        if (item.Core.ItemKind == 5 && item.Core.Value > 0 && item.CreatureDetail is null)
            throw new InvalidOperationException("A creature without its existing identity needs creation facts.");
        var facts = item.AvatarDetail is { } carriedByTheWrite
            ? FactsOf(carriedByTheWrite)
            : item.Core.ItemKind == AvatarKind
              && carried.TryGetValue(item.Core.Value, out var held)
                ? held
                : null;
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            INSERT INTO items(
                item_core,
                avatar_expire_date, avatar_clear_id, avatar_jewel_socket, avatar_color1,
                avatar_color2, avatar_delete_date,
                creature_field04, creature_mode_flag, creature_progress_value, creature_mode1_field0a,
                creature_mode1_field0b, creature_field_after_value, creature_text, creature_tail_flag)
            SELECT @core, @expire, @clear,
                @socket, @color1, @color2, @delete,
                @field04, @modeFlag, @progress, @mode1a, @mode1b, @afterValue, @creatureText, @tailFlag
            WHERE @expire IS NULL OR NOT EXISTS (
                SELECT 1 FROM items standing
                WHERE standing.avatar_expire_date IS NOT NULL
                  AND substr(standing.item_core, 6, 4) = substr(@core, 6, 4))
            RETURNING item_uid;
            """;
        command.Parameters.AddWithValue(
            "@core", InventoryItemCore.Encode(item.Core));
        foreach (var (name, index) in AvatarColumns)
        {
            command.Parameters.AddWithValue(name, facts?[index] ?? DBNull.Value);
        }
        var creature = item.CreatureDetail;
        command.Parameters.AddWithValue("@field04", (object?)creature?.Field04 ?? DBNull.Value);
        command.Parameters.AddWithValue("@modeFlag", (object?)creature?.ModeFlag ?? DBNull.Value);
        command.Parameters.AddWithValue("@progress", (object?)creature?.ProgressValue ?? DBNull.Value);
        command.Parameters.AddWithValue("@mode1a", (object?)creature?.Mode1Field0A ?? DBNull.Value);
        command.Parameters.AddWithValue("@mode1b", (object?)creature?.Mode1Field0B ?? DBNull.Value);
        command.Parameters.AddWithValue("@afterValue", (object?)creature?.FieldAfterValue ?? DBNull.Value);
        command.Parameters.AddWithValue("@creatureText", (object?)creature?.CreatureText ?? DBNull.Value);
        command.Parameters.AddWithValue("@tailFlag", (object?)creature?.TailFlag ?? DBNull.Value);

        try
        {
            var allocated = await command.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);
            var identity = allocated is null
                ? throw new InvalidOperationException(
                    "The world took the item and named no identity.")
                : checked((int)Convert.ToInt64(allocated, null));
            if (creature is not null)
            {
                // The legacy core's creature UID is the same identifier emitted by the creature
                // list. Only settlement knows the newly allocated world identity.
                await using var link = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
                link.CommandText = "UPDATE items SET item_core=@core WHERE item_uid=@uid";
                link.Parameters.AddWithValue("@core", InventoryItemCore.Encode(item.Core with { Value = identity }));
                link.Parameters.AddWithValue("@uid", identity);
                if (await link.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The new creature identity could not be linked to its core.");
            }
            return identity;
        }
        catch (SqliteException failure) when (
            failure.Message.Contains(WorldIdentityConstraint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "This database has no world identity left to give: the next one would not fit the "
                + "signed 32-bit field a creature's identity goes on the wire as.",
                failure);
        }
    }

    /// <summary>Says what this position now holds, in the one shape both kinds of content use.</summary>
    /// <remarks>
    /// The branch is on what the archive said, not on anything read back out of the row. A thing
    /// with an identity is named by that identity and there is one of it; a pile is named by its
    /// template and there are as many as the pile says.
    ///
    /// The row the bytes are on is named either way, and that is the fact that does not branch: it
    /// is what every read joins on, and it is what makes a row nothing holds answerable without
    /// asking where anything is.
    /// </remarks>
    private async ValueTask HoldAsync(
        (string Scope, int Owner, int ListType) place,
        int slotIndex,
        StoredItem item,
        int identity,
        CancellationToken cancellationToken)
    {
        var pile = item.WorldClass is ItemWorldClass.Interchangeable;
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            INSERT INTO item_slots(
                owner_scope, owner_id, list_type, slot_index, kind, id, core_uid, count)
            VALUES(@scope, @owner, @list, @slot, @kind, @id, @core, @count);
            """;
        command.Parameters.AddWithValue("@scope", place.Scope);
        command.Parameters.AddWithValue("@owner", place.Owner);
        command.Parameters.AddWithValue("@list", place.ListType);
        command.Parameters.AddWithValue("@slot", slotIndex);
        command.Parameters.AddWithValue("@kind", pile ? "template" : "world");
        command.Parameters.AddWithValue("@id", pile ? item.Core.ItemId : identity);
        command.Parameters.AddWithValue("@core", identity);
        command.Parameters.AddWithValue("@count", pile ? PileOf(item.Core) : 1);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>How many a pile holds, which is at least one because the position is occupied.</summary>
    /// <remarks>
    /// A stored count of zero or less is a row that already says nothing coherent -- the position
    /// holds something, and the projection the client is sent reads the same value the item's core
    /// carries, not this one. The slot table says one rather than refusing the whole settlement
    /// over a value it is not the authority on.
    /// </remarks>
    private static int PileOf(InventoryItemCoreState core) => Math.Max(core.Value, 1);

    /// <summary>What the database calls the bound on an identity it may allocate.</summary>
    private const string WorldIdentityConstraint = "world_identity_fits_the_wire";

    /// <summary>The kind byte an avatar's core carries.</summary>
    private const byte AvatarKind = 8;

    /// <summary>An avatar UID as the four bytes a core carries it as.</summary>
    private static byte[] AvatarKeyOf(int avatarUid)
    {
        var key = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(key, avatarUid);
        return key;
    }

    /// <summary>The avatar facts an insert binds, in the order they are read back.</summary>
    private static readonly (string Name, int Index)[] AvatarColumns =
    [
        ("@expire", 0), ("@clear", 1), ("@socket", 2),
        ("@color1", 3), ("@color2", 4), ("@delete", 5),
    ];

    /// <summary>The six facts a write carried, in the order <see cref="AvatarColumns"/> binds.</summary>
    /// <remarks>
    /// The two colours are widened to <see cref="int"/> so that a column holding an INTEGER is
    /// bound the same value whichever of the two sources it came from: what the retrieval reads
    /// back off a row is what SQLite stores, and it stores no sixteen-bit thing.
    ///
    /// <b>This method cannot be handed a null socket to bind.</b>
    /// <see cref="InventoryAvatarDetailState"/> declares <c>JewelSocket</c> as a non-nullable
    /// <c>byte[]</c> and this assembly compiles with nullable reference types on, so what is bound
    /// is the array the record carries, whatever length it is. The table is what says a socket that
    /// is present is thirty bytes, and it refuses any other length rather than this method silently
    /// correcting one. The symmetry does hold on the other source: each of the six
    /// <see cref="CarriedAvatarsAsync"/> reads back can be null and is bound as null.
    /// </remarks>
    private static object?[] FactsOf(InventoryAvatarDetailState detail) =>
    [
        detail.ExpireDate, detail.ClearAvatarId, detail.JewelSocket,
        (int)detail.Color1, (int)detail.Color2, detail.DeleteDate,
    ];

    private static InventoryItemCoreState DecodeCore(SqliteDataReader reader, int ordinal) =>
        InventoryItemCore.Decode((byte[])reader.GetValue(ordinal));

    /// <summary>The slot numbers the table will accept.</summary>
    /// <remarks>
    /// An older implementation parked a row on slot -1 for the length of a swap, and a later one
    /// parked it under a container of -1. Nothing parks anything anywhere now, and a negative slot
    /// written here would be discovered only when the character next tried to enter and the row
    /// failed validation.
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
    /// Account cargo accepts only an account-scoped identity; a character-scoped spelling is
    /// refused so the two ownership namespaces cannot alias.
    /// </remarks>
    private static (string Scope, int Owner, int ListType) OwnerOf(ContainerId containerId)
    {
        if (containerId.TryGetAccountCargoOwner(out var accountId))
            return ("account", accountId.Value, (int)InventoryContainerType.AccountCargo);

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

        return ("character", characterId.Value, (int)containerType);
    }
}
