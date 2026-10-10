using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

internal sealed class SqliteItemDomainStore : IItemDomainStore
{
    private readonly string connectionString;
    private readonly IInventoryContentsEventDomainService? contentsEvents;
    public SqliteItemDomainStore(string databasePath, IInventoryContentsEventDomainService? contentsEvents = null)
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath) }.ToString();
        this.contentsEvents = contentsEvents;
    }
    public object StoreIdentity => connectionString;
    public bool IsDurable => true;
    public IItemDomainStoreSession SessionIn(IDomainTransaction transaction)
    {
        var scope = ItemLifecycleScope.In(transaction);
        return SqliteItemDomainStoreSession.In(transaction.Enlist(StoreIdentity,
            () => new SqliteDatabaseParticipant(connectionString)), scope, contentsEvents);
    }
}

/// <summary>Immediate statements share the participant's uncommitted database transaction.</summary>
internal sealed partial class SqliteItemDomainStoreSession(SqliteDatabaseParticipant owner, ItemLifecycleScope scope)
    : IItemDomainStoreSession, ISqliteStoreFacet
{
    private readonly object identity = new();
    private readonly Dictionary<(ContainerId, int), long> generations = [];
    private readonly Dictionary<ItemRecord, long> detached = new(ReferenceEqualityComparer.Instance);
    private bool closed;
    private bool faulted;
    private bool changed;
    private IInventoryContentsEventDomainService? contentsEvents;
    internal static SqliteItemDomainStoreSession In(SqliteDatabaseParticipant owner, ItemLifecycleScope scope,
        IInventoryContentsEventDomainService? contentsEvents = null)
    {
        var session = owner.Facet(typeof(SqliteItemDomainStoreSession), db => new SqliteItemDomainStoreSession(db, scope));
        session.contentsEvents ??= contentsEvents;
        return session;
    }
    private ValueTask ContentsChangedAsync(ContainerId container, CancellationToken ct) =>
        contentsEvents?.RecordAsync(container, ct) ?? ValueTask.CompletedTask;
    public bool HasStagedWork => changed;
    private void Active()
    {
        scope.EnsureActive();
        if (closed || faulted) throw new InvalidOperationException("The item session is no longer usable.");
    }
    private long Generation(ContainerId container, int slot) => generations.GetValueOrDefault((container, slot));
    private void Advance(ContainerId container, int slot) => generations[(container, slot)] = checked(Generation(container, slot) + 1);
    private ItemReference Reference(ContainerId container, int slot, ItemSnapshot snapshot) =>
        new(identity, container, slot, Generation(container, slot), snapshot);

    public async ValueTask<ItemReference?> GetAsync(ContainerId container, int slot, CancellationToken ct)
    {
        Active();
        var row = await ReadAsync(container, slot, ct).ConfigureAwait(false);
        return row is null ? null : Reference(container, slot, row.Value.Snapshot);
    }
    public async ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(ContainerId container, CancellationToken ct)
    {
        Active();
        var place = OwnerOf(container);
        var slots = new List<int>();
        await using (var command = await owner.CommandAsync(ct).ConfigureAwait(false))
        {
            command.CommandText = "SELECT slot_index FROM item_slots WHERE owner_scope=@scope AND owner_id=@owner AND list_type=@list ORDER BY slot_index";
            BindPlace(command, place);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) slots.Add(reader.GetInt32(0));
        }
        var result = new List<ItemReference>(slots.Count);
        foreach (var slot in slots) result.Add((await GetAsync(container, slot, ct).ConfigureAwait(false))!);
        return result;
    }
    private async ValueTask<(long Uid, ItemSnapshot Snapshot)> ResolveAsync(ItemReference reference, CancellationToken ct)
    {
        Active();
        if (!reference.BelongsTo(identity) || reference.Generation != Generation(reference.ContainerId, reference.StorageSlotIndex))
            throw new InvalidOperationException("The item reference no longer identifies this occupancy.");
        return await ReadAsync(reference.ContainerId, reference.StorageSlotIndex, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The referenced slot is empty.");
    }
    public async ValueTask<ItemRecord> CreateAsync(ItemSnapshot snapshot, CancellationToken ct)
    {
        Active();
        snapshot = snapshot.DeepCopy();
        if (snapshot.ItemUid is not null) throw new InvalidOperationException("Creation cannot supply an existing item identity.");
        Validate(snapshot);
        return await Mutate(async () =>
        {
            var uid = await InsertAsync(snapshot, ct).ConfigureAwait(false);
            if (!snapshot.IsPile) snapshot = snapshot with { ItemUid = new ItemIdentity(checked((int)uid)) };
            if (snapshot.CreatureDetail is not null)
            {
                snapshot = snapshot with { InstanceValue = checked((int)uid) };
                await SaveAsync(uid, snapshot, ct).ConfigureAwait(false);
            }
            return Issue(uid, snapshot);
        }).ConfigureAwait(false);
    }
    public async ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct)
    {
        var row = await ResolveAsync(reference, ct).ConfigureAwait(false);
        var amount = count ?? row.Snapshot.Count;
        if (amount <= 0 || amount > row.Snapshot.Count || (!row.Snapshot.IsPile && amount != 1))
            throw new ArgumentOutOfRangeException(nameof(count));
        return await Mutate(async () =>
        {
            if (amount == row.Snapshot.Count)
            {
                await DeleteSlotAsync(reference.ContainerId, reference.StorageSlotIndex, ct).ConfigureAwait(false);
                Advance(reference.ContainerId, reference.StorageSlotIndex);
                await ContentsChangedAsync(reference.ContainerId, ct).ConfigureAwait(false);
                return Issue(row.Uid, row.Snapshot);
            }
            var remainder = row.Snapshot with { Count = row.Snapshot.Count - amount };
            await SaveAsync(row.Uid, remainder, ct).ConfigureAwait(false);
            await SetSlotCountAsync(row.Uid, remainder.Count, ct).ConfigureAwait(false);
            var piece = row.Snapshot with { Count = amount };
            await ContentsChangedAsync(reference.ContainerId, ct).ConfigureAwait(false);
            return Issue(await InsertAsync(piece, ct).ConfigureAwait(false), piece);
        }).ConfigureAwait(false);
    }
    public async ValueTask<ItemReference> PutAsync(ContainerId container, int slot, ItemRecord record, CancellationToken ct)
    {
        Active();
        var foreign = scope.IsForeign(record, identity);
        var uid = foreign ? 0 : Detached(record);
        var incoming = record.Snapshot;
        Validate(incoming);
        ValidatePosition(container, slot, incoming);
        var occupied = await ReadAsync(container, slot, ct).ConfigureAwait(false);
        if (occupied is { } held && !held.Snapshot.CanStackWith(incoming))
            throw new InvalidOperationException("The destination does not accept this item.");
        var total = occupied is { } pile ? checked(pile.Snapshot.Count + incoming.Count) : incoming.Count;
        return await Mutate(async () =>
        {
            if (occupied is { } target)
            {
                var merged = target.Snapshot with { Count = total };
                await SaveAsync(target.Uid, merged, ct).ConfigureAwait(false);
                await SetSlotCountAsync(target.Uid, PhysicalCount(merged), ct).ConfigureAwait(false);
                if (!foreign) await RemoveRowAsync(uid, ct).ConfigureAwait(false);
                await scope.AcceptAsync(record, identity, ct).ConfigureAwait(false);
                detached.Remove(record);
                await ContentsChangedAsync(container, ct).ConfigureAwait(false);
                return Reference(container, slot, merged);
            }
            if (foreign) uid = await InsertAsync(incoming, ct).ConfigureAwait(false);
            await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
            command.CommandText = "INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) VALUES(@scope,@owner,@list,@slot,@kind,@id,@uid,@count)";
            BindPlace(command, OwnerOf(container));
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@kind", incoming.IsPile ? "template" : "world");
            command.Parameters.AddWithValue("@id", incoming.IsPile ? incoming.ItemId : uid);
            command.Parameters.AddWithValue("@uid", uid);
            command.Parameters.AddWithValue("@count", PhysicalCount(incoming));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await scope.AcceptAsync(record, identity, ct).ConfigureAwait(false);
            detached.Remove(record);
            Advance(container, slot);
            await ContentsChangedAsync(container, ct).ConfigureAwait(false);
            return Reference(container, slot, incoming);
        }).ConfigureAwait(false);
    }
    public async ValueTask<ItemReference> UpdateAsync(ItemReference reference, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        var row = await ResolveAsync(reference, ct).ConfigureAwait(false);
        var next = change(row.Snapshot.DeepCopy()).DeepCopy();
        Validate(next);
        ValidatePosition(reference.ContainerId, reference.StorageSlotIndex, next);
        if (next.IsMirror != row.Snapshot.IsMirror || next.MirrorSourceUid != row.Snapshot.MirrorSourceUid
            || next.ItemUid != row.Snapshot.ItemUid || next.IsPile != row.Snapshot.IsPile || next.Type != row.Snapshot.Type
            || next.LegacyKind != row.Snapshot.LegacyKind
            || (next.ItemId != row.Snapshot.ItemId && (next.IsPile || next.Type != ItemTraits.Creature || next.LegacyKind != 5))
            || (!next.IsPile && next.Type is ItemTraits.Avatar or ItemTraits.Creature && next.InstanceValue != row.Snapshot.InstanceValue))
            throw new InvalidOperationException("An update cannot replace item identity or its identity shape.");
        return await Mutate(async () =>
        {
            await SaveAsync(row.Uid, next, ct).ConfigureAwait(false);
            await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
            command.CommandText = "UPDATE item_slots SET count=@count,id=@id WHERE core_uid=@uid";
            command.Parameters.AddWithValue("@count", PhysicalCount(next));
            command.Parameters.AddWithValue("@id", next.IsPile ? next.ItemId : row.Uid);
            command.Parameters.AddWithValue("@uid", row.Uid);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            if (next.ItemId != row.Snapshot.ItemId || next.Count != row.Snapshot.Count)
                await ContentsChangedAsync(reference.ContainerId, ct).ConfigureAwait(false);
            var result = Reference(reference.ContainerId, reference.StorageSlotIndex, next);
            return reference.BusinessSlot is { } business ? result.WithBusinessSlot(business) : result;
        }).ConfigureAwait(false);
    }
    public ValueTask DeleteItemAsync(ItemRecord record, CancellationToken ct)
    {
        Active();
        _ = Detached(record);
        return scope.DeleteItemAsync(record, ct);
    }
    private ItemRecord Issue(long uid, ItemSnapshot snapshot)
    {
        ItemRecord? record = null;
        record = scope.Issue(snapshot, identity, async ct =>
        {
            Active();
            await Mutate(async () =>
            {
                await RemoveRowAsync(uid, ct).ConfigureAwait(false);
                detached.Remove(record!);
                return true;
            }).ConfigureAwait(false);
        }, (count, ct) => SplitDetachedAsync(record!, uid, snapshot, count, ct));
        detached.Add(record, uid);
        return record;
    }
    private async ValueTask<(ItemRecord Taken, ItemRecord Remainder)> SplitDetachedAsync(
        ItemRecord record, long uid, ItemSnapshot snapshot, int count, CancellationToken ct)
    {
        Active();
        _ = Detached(record);
        return await Mutate(async () =>
        {
            var taken = snapshot with { Count = count };
            var remainder = snapshot with { Count = snapshot.Count - count };
            await SaveAsync(uid, remainder, ct).ConfigureAwait(false);
            var takenUid = await InsertAsync(taken, ct).ConfigureAwait(false);
            await scope.AcceptAsync(record, identity, ct).ConfigureAwait(false);
            detached.Remove(record);
            return (Issue(takenUid, taken), Issue(uid, remainder));
        }).ConfigureAwait(false);
    }
    private long Detached(ItemRecord record) => scope.IsPending(record) && detached.TryGetValue(record, out var uid) ? uid
        : throw new InvalidOperationException("This record was not issued here or was already received or deleted.");
    private async ValueTask<T> Mutate<T>(Func<ValueTask<T>> operation)
    {
        try { changed = true; return await operation().ConfigureAwait(false); }
        catch { faulted = true; throw; }
    }
    public void ValidateForCommit() { Active(); scope.ValidateForCommit(); }
    public ValueTask ApplyDecidedWritesAsync(CancellationToken ct) { Active(); ct.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
    public async ValueTask FlushAsync(CancellationToken ct)
    {
        await ApplyDecidedWritesAsync(ct).ConfigureAwait(false);
        await ValidateMailCustodyAsync(ct).ConfigureAwait(false);
    }
    public void Discard() { closed = true; detached.Clear(); generations.Clear(); }

    private async ValueTask<(long Uid, ItemSnapshot Snapshot)?> ReadAsync(ContainerId container, int slot, CancellationToken ct)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = """
            SELECT i.item_uid,i.item_core,s.kind,s.count,
                i.avatar_expire_date,i.avatar_clear_id,i.avatar_jewel_socket,i.avatar_color1,i.avatar_color2,i.avatar_delete_date,
                i.creature_field04,i.creature_mode_flag,i.creature_progress_value,i.creature_mode1_field0a,
                i.creature_mode1_field0b,i.creature_field_after_value,i.creature_text,i.creature_tail_flag,i.is_mirror,i.mirror_source_uid,i.appearance_item_id
            FROM item_slots s JOIN items i ON i.item_uid=s.core_uid
            WHERE s.owner_scope=@scope AND s.owner_id=@owner AND s.list_type=@list AND s.slot_index=@slot
            """;
        BindPlace(command, OwnerOf(container)); command.Parameters.AddWithValue("@slot", slot);
        await using var r = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        var uid = r.GetInt64(0);
        var core = InventoryItemCore.Decode((byte[])r.GetValue(1));
        var pile = r.GetString(2) == "template";
        var avatar = ReadAvatarDetail(r, 4);
        var creature = r.IsDBNull(10) ? null : new InventoryCreatureDetailState(r.GetInt32(10), checked((byte)r.GetInt32(11)), r.GetInt32(12), r.GetInt32(13), r.GetInt32(14), r.GetInt32(15), r.IsDBNull(16) ? [] : (byte[])r.GetValue(16), checked((byte)r.GetInt32(17)));
        var snapshot = ItemSnapshot.FromCore(core, pile, new ItemIdentity(checked((int)uid)), avatar, creature) with
        { IsMirror = r.GetInt32(18) != 0, MirrorSourceUid = r.IsDBNull(19) ? null : new(checked((int)r.GetInt64(19))), AppearanceItemId = r.IsDBNull(20) ? null : r.GetInt32(20) };
        ValidatePosition(container, slot, snapshot);
        if (PhysicalCount(snapshot) != r.GetInt32(3)) throw new InvalidDataException("Item count disagrees with its slot.");
        var legacyCargoAvatar = container.TryGetAccountOwner(out _, out var list)
            && list == InventoryContainerType.AccountCargo && snapshot.AvatarDetail is null;
        Validate(snapshot, legacyCargoAvatar);
        return (uid, snapshot);
    }
    private static InventoryAvatarDetailState? ReadAvatarDetail(SqliteDataReader reader, int offset)
    {
        if (reader.IsDBNull(offset)) return null;
        var color1 = reader.GetInt64(offset + 3);
        var color2 = reader.GetInt64(offset + 4);
        if (color1 is < 0 or > ushort.MaxValue || color2 is < 0 or > ushort.MaxValue)
            throw new InvalidDataException("Avatar detail has an out-of-range color.");
        return new InventoryAvatarDetailState(reader.GetInt32(offset), reader.GetInt32(offset + 1),
            (byte[])reader.GetValue(offset + 2), (ushort)color1, (ushort)color2, reader.GetInt32(offset + 5));
    }
    private static bool IsRevivalCounter(ItemSnapshot value) => value is { IsPile: true, LegacyKind: 0, ItemId: 1, Type: ItemTraits.None };
    private static int PhysicalCount(ItemSnapshot value) => IsRevivalCounter(value) ? Math.Max(value.Count, 1) : value.Count;
    private static void ValidatePosition(ContainerId container, int slot, ItemSnapshot value)
    {
        if (value.IsMirror && (value.IsPile || !container.TryGetCharacterOwner(out _, out var mirrorList)
            || mirrorList != InventoryContainerType.Equipment))
            throw new InvalidDataException("A mirror may only occupy a character equipment slot.");
        if (value.Count == 0 && (!IsRevivalCounter(value) || slot != 1
            || !container.TryGetCharacterOwner(out _, out var type) || type != InventoryContainerType.Main))
            throw new InvalidDataException("A zero counter can only occupy the character revival slot.");
    }
    private static void Validate(ItemSnapshot value, bool allowLegacyCargoAvatar = false)
    {
        if (value.AppearanceItemId is < 0 || value.AppearanceItemId == 0 && value.LegacyKind != 8) throw new InvalidDataException("Invalid item appearance identity.");
        if (value.ItemId <= 0 || (value.Count < 0 || value.Count == 0 && !IsRevivalCounter(value)) || (!value.IsPile && value.Count != 1)
            || (value.IsPile && value.ItemUid is not null)) throw new InvalidDataException("Invalid item identity or quantity.");
        if (value.Type != ItemTraitClassification.For(value.LegacyKind, value.ItemId))
            throw new InvalidDataException("Item type disagrees with its legacy encoding.");
        var avatar = !value.IsPile && value.Type == ItemTraits.Avatar;
        var creature = !value.IsPile && value.Type == ItemTraits.Creature;
        if (avatar && value.InstanceValue <= 0)
            throw new InvalidDataException("An avatar requires a positive legacy identity.");
        if ((avatar != (value.AvatarDetail is not null) && !(avatar && allowLegacyCargoAvatar))
            || creature != (value.CreatureDetail is not null))
            throw new InvalidDataException("Instance details do not match the item type.");
        if (value.AvatarDetail is { JewelSocket.Length: not 30 }) throw new InvalidDataException("Invalid avatar socket size.");
        if (value.CreatureDetail is { } pet && (pet.CreatureText.Length > 4096 || pet.Field04 is < 0 or > 255 || pet.ProgressValue < 0
            || pet.FieldAfterValue is < 0 or > 255 || pet.Mode1Field0A is < 0 or > 255 || pet.Mode1Field0B is < 0 or > 255))
            throw new InvalidDataException("Invalid creature details.");
    }
    private async ValueTask<long> InsertAsync(ItemSnapshot snapshot, CancellationToken ct)
    {
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = """
            INSERT INTO items(item_core,avatar_expire_date,avatar_clear_id,avatar_jewel_socket,avatar_color1,avatar_color2,avatar_delete_date,
                creature_field04,creature_mode_flag,creature_progress_value,creature_mode1_field0a,creature_mode1_field0b,creature_field_after_value,creature_text,creature_tail_flag,is_mirror,mirror_source_uid,appearance_item_id)
            SELECT @core,@expire,@clear,@socket,@color1,@color2,@delete,@field04,@mode,@progress,@mode1a,@mode1b,@after,@text,@tail,@mirror,@mirrorSource,@appearance
            WHERE @expire IS NULL OR NOT EXISTS(SELECT 1 FROM items WHERE avatar_expire_date IS NOT NULL AND substr(item_core,6,4)=substr(@core,6,4))
            RETURNING item_uid;
            """;
        BindSnapshot(command, snapshot);
        var uid = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return uid is null ? throw new InvalidOperationException("An avatar with this legacy identity already exists.") : Convert.ToInt64(uid);
    }
    private async ValueTask SaveAsync(long uid, ItemSnapshot snapshot, CancellationToken ct)
    {
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = """
            UPDATE items SET item_core=@core,avatar_expire_date=@expire,avatar_clear_id=@clear,avatar_jewel_socket=@socket,
                avatar_color1=@color1,avatar_color2=@color2,avatar_delete_date=@delete,creature_field04=@field04,
                creature_mode_flag=@mode,creature_progress_value=@progress,creature_mode1_field0a=@mode1a,creature_mode1_field0b=@mode1b,
                creature_field_after_value=@after,creature_text=@text,creature_tail_flag=@tail,is_mirror=@mirror,mirror_source_uid=@mirrorSource,appearance_item_id=@appearance,updated_at=CURRENT_TIMESTAMP WHERE item_uid=@uid
            """;
        BindSnapshot(command, snapshot); command.Parameters.AddWithValue("@uid", uid);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1) throw new InvalidDataException("Item row disappeared.");
    }
    private static void BindSnapshot(SqliteCommand command, ItemSnapshot snapshot)
    {
        var a = snapshot.AvatarDetail; var c = snapshot.CreatureDetail;
        command.Parameters.AddWithValue("@appearance", snapshot.AppearanceItemId is { } appearance ? appearance : DBNull.Value);
        command.Parameters.AddWithValue("@mirror", snapshot.IsMirror ? 1 : 0);
        command.Parameters.AddWithValue("@mirrorSource", snapshot.MirrorSourceUid is { } source ? source.Value : DBNull.Value);
        command.Parameters.AddWithValue("@core", InventoryItemCore.Encode(snapshot.ToCore()));
        (string, object?)[] values = [("@expire",a?.ExpireDate),("@clear",a?.ClearAvatarId),("@socket",a?.JewelSocket),
            ("@color1",a?.Color1),("@color2",a?.Color2),("@delete",a?.DeleteDate),("@field04",c?.Field04),("@mode",c?.ModeFlag),
            ("@progress",c?.ProgressValue),("@mode1a",c?.Mode1Field0A),("@mode1b",c?.Mode1Field0B),("@after",c?.FieldAfterValue),("@text",c?.CreatureText),("@tail",c?.TailFlag)];
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
    private async ValueTask DeleteSlotAsync(ContainerId container, int slot, CancellationToken ct)
    {
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = "DELETE FROM item_slots WHERE owner_scope=@scope AND owner_id=@owner AND list_type=@list AND slot_index=@slot";
        BindPlace(command, OwnerOf(container)); command.Parameters.AddWithValue("@slot", slot);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1) throw new InvalidDataException("Item slot disappeared.");
    }
    private async ValueTask SetSlotCountAsync(long uid, int count, CancellationToken ct)
    {
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = "UPDATE item_slots SET count=@count WHERE core_uid=@uid";
        command.Parameters.AddWithValue("@count", count); command.Parameters.AddWithValue("@uid", uid);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1) throw new InvalidDataException("Item does not have exactly one position.");
    }
    private async ValueTask RemoveRowAsync(long uid, CancellationToken ct)
    {
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = "DELETE FROM items WHERE item_uid=@uid AND NOT EXISTS(SELECT 1 FROM item_slots WHERE core_uid=@uid)";
        command.Parameters.AddWithValue("@uid", uid);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1) throw new InvalidOperationException("Only an unheld item can be deleted.");
    }
    private static void BindPlace(SqliteCommand command, (string Scope, int Owner, int List) place)
    {
        command.Parameters.AddWithValue("@scope", place.Scope); command.Parameters.AddWithValue("@owner", place.Owner); command.Parameters.AddWithValue("@list", place.List);
    }
    private static (string Scope, int Owner, int List) OwnerOf(ContainerId container)
    {
        if (!container.TryGetOwner(out var owner, out var type)
            || type is not (InventoryContainerType.Main or InventoryContainerType.Avatar or InventoryContainerType.PersonalCargo or InventoryContainerType.Equipment or InventoryContainerType.Pet or InventoryContainerType.AccountCargo or InventoryContainerType.SharedMaterial
                or InventoryContainerType.TitleBookGeneral or InventoryContainerType.TitleBookSpecific or InventoryContainerType.TitleBookPvp or InventoryContainerType.TitleBookDespair or InventoryContainerType.TitleBookEvent)
            || type is >= InventoryContainerType.TitleBookGeneral and <= InventoryContainerType.TitleBookEvent && owner.Scope != InventoryOwnerType.Character
            || type is InventoryContainerType.AccountCargo or InventoryContainerType.SharedMaterial && owner.Scope != InventoryOwnerType.Account)
            throw new InvalidOperationException("The container has no supported persistent owner.");
        return (owner.Scope == InventoryOwnerType.Account ? "account" : "character", owner.Value, (int)type);
    }
}
