using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Creatures;

internal sealed class SqliteCreatureDomainStore(string databasePath,
    IInventoryContentsEventDomainService? contentsEvents = null) : ICreatureDomainStore
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
        { DataSource = Path.GetFullPath(databasePath) }.ToString();
    public object StoreIdentity => connectionString;
    public bool IsDurable => true;
    public ICreatureDomainStoreSession SessionIn(IDomainTransaction transaction)
    {
        var scope = ItemLifecycleScope.In(transaction);
        var database = transaction.Enlist(StoreIdentity, () => new SqliteDatabaseParticipant(connectionString));
        return SqliteCreatureDomainStoreSession.In(database, scope, contentsEvents);
    }
}

/// <summary>Creature projections and the entry guard share the item's one state writer.</summary>
internal sealed class SqliteCreatureDomainStoreSession(SqliteDatabaseParticipant owner,
    IItemDomainStoreSession items, ItemLifecycleScope scope) : ICreatureDomainStoreSession, ISqliteStoreFacet
{
    private readonly Dictionary<(CharacterId Owner, int Uid), (int? AccountId, int Stomach,
        bool OnlyWhenAtMostZero)> conditions = [];

    internal static SqliteCreatureDomainStoreSession In(SqliteDatabaseParticipant owner, ItemLifecycleScope scope,
        IInventoryContentsEventDomainService? contentsEvents = null)
    {
        // Acquire before prepare: a transaction may not enlist another participant during settlement.
        var items = SqliteItemDomainStoreSession.In(owner, scope, contentsEvents);
        return owner.Facet(typeof(SqliteCreatureDomainStoreSession), db => new SqliteCreatureDomainStoreSession(db, items, scope));
    }
    public bool HasStagedWork => conditions.Count > 0;

    public async ValueTask<ItemReference?> FindItemAsync(CharacterId character, int uid, CancellationToken ct) =>
        (await FindItemsAsync(character, uid, false, ct).ConfigureAwait(false)).SingleOrDefault();

    public async ValueTask<ItemReference> ReadItemAsync(ItemReference reference, CancellationToken ct)
    {
        var current = await items.GetAsync(reference.ContainerId, reference.StorageSlotIndex, ct).ConfigureAwait(false);
        if (current is null || !current.SameOccupancyAs(reference))
            throw new InvalidOperationException("The creature reference no longer identifies this occupancy.");
        _ = CreatureState.FromItem(current.Snapshot);
        return reference.BusinessSlot is { } slot ? current.WithBusinessSlot(slot) : current;
    }

    public async ValueTask<CreatureState?> ReadAsync(CharacterId character, int uid, CancellationToken ct)
    {
        var item = await FindItemAsync(character, uid, ct).ConfigureAwait(false);
        return item is null ? null : Project(character, item);
    }
    public async ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(CharacterId character, CancellationToken ct) =>
        (await FindItemsAsync(character, null, false, ct).ConfigureAwait(false)).Select(item => Project(character, item)).ToArray();
    public async ValueTask<CreatureState?> ReadCurrentAsync(CharacterId character, CancellationToken ct)
    {
        var item = (await FindItemsAsync(character, null, true, ct).ConfigureAwait(false)).SingleOrDefault();
        return item is null ? null : Project(character, item);
    }

    public async ValueTask<ItemReference> WriteAsync(ItemReference reference, CreatureState creature,
        int? expiration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(creature);
        if (creature.ItemUid <= 0 || creature.ItemTemplateId <= 0 || creature.Stomach is < 0 or > byte.MaxValue
            || creature.Experience < 0 || creature.Level is < 0 or > byte.MaxValue || creature.NameBytes is null
            || creature.NameBytes.Length > 4096 || creature.ModeFlag is < 0 or > byte.MaxValue
            || creature.Mode1Field0A is < 0 or > byte.MaxValue || creature.Mode1Field0B is < 0 or > byte.MaxValue
            || creature.TailFlag is < 0 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(creature));
        var current = await ReadItemAsync(reference, ct).ConfigureAwait(false);
        if (current.ItemUid?.Value != creature.ItemUid)
            throw new InvalidOperationException("Creature identity cannot be replaced.");
        return await items.UpdateAsync(current, item => item with
        {
            ItemId = creature.ItemTemplateId, CreatureDetail = creature.ToDetail(),
            ExpireTime = expiration ?? item.ExpireTime
        }, ct).ConfigureAwait(false);
    }

    internal void WriteCondition(OwnedCharacterId place, long uid, int stomach, bool onlyWhenAtMostZero) =>
        StageCondition(place.Character, checked((int)uid), place.Account.Value, stomach, onlyWhenAtMostZero);
    public void ReviveIfEmpty(CharacterId character, int uid) => StageCondition(character, uid, null, 1, true);
    private void StageCondition(CharacterId character, int uid, int? account, int stomach, bool onlyDead)
    {
        scope.EnsureActive();
        if (uid <= 0 || stomach is < 0 or > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(uid));
        conditions[(character, uid)] = (account, stomach, onlyDead);
    }

    public async ValueTask FlushAsync(CancellationToken ct)
    {
        foreach (var ((character, uid), (account, stomach, onlyDead)) in conditions)
        {
            await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
            command.CommandText = "SELECT 1 FROM characters WHERE character_id=@owner AND (@account IS NULL OR account_id=@account) AND delete_flag=0";
            command.Parameters.AddWithValue("@owner", character.Value);
            command.Parameters.AddWithValue("@account", account is null ? DBNull.Value : account.Value);
            if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
                throw new InvalidDataException("Equipped creature revival owner changed.");
            var reference = await FindItemAsync(character, uid, ct).ConfigureAwait(false);
            if (reference is null || (onlyDead && reference.Snapshot.CreatureDetail!.Field04 > 0))
                throw new InvalidDataException("Equipped creature revival changed concurrently.");
            await items.UpdateAsync(reference, item => item with
            { CreatureDetail = item.CreatureDetail! with { Field04 = stomach } }, ct).ConfigureAwait(false);
        }
        conditions.Clear();
    }
    public void Discard() => conditions.Clear();

    private CreatureState Project(CharacterId character, ItemReference item)
    {
        var state = CreatureState.FromItem(item.Snapshot);
        return conditions.TryGetValue((character, state.ItemUid), out var condition)
            ? state with { Stomach = condition.Stomach, ExactStomach = condition.Stomach } : state;
    }
    private async ValueTask<IReadOnlyList<ItemReference>> FindItemsAsync(CharacterId character, int? uid,
        bool currentOnly, CancellationToken ct)
    {
        scope.EnsureActive();
        var locations = new List<(InventoryContainerType Type, int Slot)>();
        await using (var command = await owner.CommandAsync(ct).ConfigureAwait(false))
        {
            command.CommandText = """
                SELECT place.list_type, place.slot_index FROM item_slots place JOIN items held ON held.item_uid=place.core_uid
                WHERE place.owner_scope='character' AND place.owner_id=@owner AND held.creature_field04 IS NOT NULL
                AND (@uid IS NULL OR held.item_uid=@uid)
                AND (@current=0 OR (place.list_type=3 AND place.slot_index=25)) ORDER BY held.item_uid
                """;
            command.Parameters.AddWithValue("@owner", character.Value);
            command.Parameters.AddWithValue("@uid", uid is null ? DBNull.Value : uid.Value);
            command.Parameters.AddWithValue("@current", currentOnly ? 1 : 0);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                locations.Add(((InventoryContainerType)reader.GetInt32(0), reader.GetInt32(1)));
        }
        var result = new List<ItemReference>();
        foreach (var (type, slot) in locations)
        {
            var item = await items.GetAsync(ContainerId.ForCharacter(character, type), slot, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Creature location disappeared.");
            if (item.Snapshot.LegacyKind != 5) throw new InvalidDataException("Creature detail has no matching item kind.");
            result.Add(item);
        }
        return result;
    }
}
