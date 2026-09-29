using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Creatures;

internal sealed class SqliteCreatureDomainStore : ICreatureDomainStore
{
    private readonly string connectionString;

    public SqliteCreatureDomainStore(string databasePath)
    {
        this.connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath)
        }.ToString();
    }

    public object StoreIdentity => this.connectionString;
    public bool IsDurable => true;

    public ICreatureDomainStoreSession SessionIn(IDomainTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var database = transaction.Enlist(this.StoreIdentity,
            () => new SqliteDatabaseParticipant(this.connectionString));
        return SqliteCreatureDomainStoreSession.In(database);
    }
}

internal sealed class SqliteCreatureDomainStoreSession(SqliteDatabaseParticipant owner)
    : ICreatureDomainStoreSession, ISqliteStoreFacet
{
    private static readonly object FacetKey = typeof(SqliteCreatureDomainStoreSession);
    private readonly Dictionary<(CharacterId Owner, int Uid), CreatureState> staged = [];
    private readonly Dictionary<(CharacterId Owner, int Uid), (int? AccountId, int Stomach,
        bool OnlyWhenAtMostZero)> conditionWrites = [];

    internal static SqliteCreatureDomainStoreSession In(SqliteDatabaseParticipant owner) =>
        owner.Facet(FacetKey, static database => new SqliteCreatureDomainStoreSession(database));

    public bool HasStagedWork => this.staged.Count > 0 || this.conditionWrites.Count > 0;

    public async ValueTask<CreatureState?> ReadAsync(
        CharacterId characterId, int itemUid, CancellationToken cancellationToken)
    {
        await this.ApplyPendingItemPositionsAsync(cancellationToken).ConfigureAwait(false);
        if (this.staged.TryGetValue((characterId, itemUid), out var staged))
            return Copy(staged);

        var value = (await this.ReadRowsAsync(characterId, itemUid, false, cancellationToken)
            .ConfigureAwait(false)).SingleOrDefault();
        return value is null ? null : ApplyCondition(characterId, value);
    }

    public async ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        await this.ApplyPendingItemPositionsAsync(cancellationToken).ConfigureAwait(false);
        var rows = (await this.ReadRowsAsync(characterId, null, false, cancellationToken)
            .ConfigureAwait(false)).ToDictionary(row => row.ItemUid, row => ApplyCondition(characterId, row));
        foreach (var ((ownerId, uid), value) in this.staged)
            if (ownerId == characterId && rows.ContainsKey(uid)) rows[uid] = Copy(value);
        return [.. rows.Values.OrderBy(row => row.ItemUid)];
    }

    public async ValueTask<CreatureState?> ReadCurrentAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        await this.ApplyPendingItemPositionsAsync(cancellationToken).ConfigureAwait(false);
        var value = (await this.ReadRowsAsync(characterId, null, true, cancellationToken)
            .ConfigureAwait(false)).SingleOrDefault();
        if (value is null) return null;
        return this.staged.TryGetValue((characterId, value.ItemUid), out var staged)
            ? Copy(staged) : ApplyCondition(characterId, value);
    }

    public void Write(CharacterId characterId, CreatureState creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        if (creature.ItemUid <= 0 || creature.ItemTemplateId <= 0
            || creature.Stomach is < 0 or > byte.MaxValue || creature.Experience < 0
            || creature.Level is < 0 or > byte.MaxValue || creature.NameBytes is null
            || creature.NameBytes.Length > 4096 || creature.ModeFlag is < 0 or > byte.MaxValue
            || creature.Mode1Field0A is < 0 or > byte.MaxValue
            || creature.Mode1Field0B is < 0 or > byte.MaxValue
            || creature.TailFlag is < 0 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(creature), "Invalid creature state.");
        if (this.conditionWrites.ContainsKey((characterId, creature.ItemUid)))
            throw new InvalidOperationException("Creature condition is already staged by the entry adapter.");
        this.staged[(characterId, creature.ItemUid)] = Copy(creature);
    }

    internal void WriteCondition(OwnedCharacterId place, long itemUid, int stomach,
        bool onlyWhenAtMostZero)
    {
        var uid = checked((int)itemUid);
        if (uid <= 0 || stomach is < 0 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(stomach));
        if (this.staged.ContainsKey((place.Character, uid)))
            throw new InvalidOperationException("Creature state is already staged by its domain store.");
        this.conditionWrites[(place.Character, uid)] = (place.Account.Value, stomach, onlyWhenAtMostZero);
    }

    public void ReviveIfEmpty(CharacterId characterId, int itemUid)
    {
        if (itemUid <= 0) throw new ArgumentOutOfRangeException(nameof(itemUid));
        if (this.staged.ContainsKey((characterId, itemUid)))
            throw new InvalidOperationException("Creature state is already staged by its domain store.");
        this.conditionWrites[(characterId, itemUid)] = (null, 1, true);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        await this.ApplyPendingItemPositionsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var ((characterId, uid), value) in this.staged)
        {
            await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
            command.CommandText = """
                UPDATE items SET creature_field04=@stomach, creature_mode_flag=@mode,
                    creature_progress_value=@experience, creature_mode1_field0a=@mode1a,
                    creature_mode1_field0b=@mode1b, creature_field_after_value=@level,
                    creature_text=@name, creature_tail_flag=@tail
                WHERE item_uid=@uid AND creature_field04 IS NOT NULL
                  AND substr(item_core, 1, 1)=X'05'
                  AND substr(item_core, 2, 4)=@template
                  AND substr(item_core, 6, 4)=@identity
                  AND EXISTS (SELECT 1 FROM item_slots place
                      WHERE place.owner_scope='character' AND place.owner_id=@owner
                        AND place.core_uid=@uid);
                """;
            command.Parameters.AddWithValue("@uid", uid);
            command.Parameters.AddWithValue("@owner", characterId.Value);
            command.Parameters.AddWithValue("@template", TemplateBytes(value.ItemTemplateId));
            command.Parameters.AddWithValue("@identity", TemplateBytes(uid));
            command.Parameters.AddWithValue("@stomach", value.Stomach);
            command.Parameters.AddWithValue("@experience", value.Experience);
            command.Parameters.AddWithValue("@level", value.Level);
            command.Parameters.AddWithValue("@name", value.NameBytes);
            command.Parameters.AddWithValue("@mode", value.ModeFlag);
            command.Parameters.AddWithValue("@mode1a", value.Mode1Field0A);
            command.Parameters.AddWithValue("@mode1b", value.Mode1Field0B);
            command.Parameters.AddWithValue("@tail", value.TailFlag);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException("Creature identity, template or owner changed.");
        }

        foreach (var ((characterId, uid), (accountId, stomach, onlyWhenAtMostZero)) in this.conditionWrites)
        {
            await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
            command.CommandText = """
                UPDATE items SET creature_field04=@stomach
                WHERE item_uid=@uid AND creature_field04 IS NOT NULL
                  AND (@onlyDead=0 OR creature_field04<=0)
                  AND EXISTS (SELECT 1 FROM item_slots slot
                      JOIN characters character ON character.character_id=slot.owner_id
                      WHERE slot.owner_scope='character' AND slot.owner_id=@owner
                        AND slot.core_uid=@uid AND (@account IS NULL OR character.account_id=@account)
                        AND character.delete_flag=0);
                """;
            command.Parameters.AddWithValue("@stomach", stomach);
            command.Parameters.AddWithValue("@uid", uid);
            command.Parameters.AddWithValue("@owner", characterId.Value);
            command.Parameters.AddWithValue("@account", accountId is null ? DBNull.Value : accountId.Value);
            command.Parameters.AddWithValue("@onlyDead", onlyWhenAtMostZero ? 1 : 0);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException("Equipped creature revival changed concurrently.");
        }
    }

    public void Discard()
    {
        this.staged.Clear();
        this.conditionWrites.Clear();
    }

    // A pending move changes ownership/current selection; materialize it inside this same
    // uncommitted database transaction before asking the item_slots table for creature state.
    private ValueTask ApplyPendingItemPositionsAsync(CancellationToken cancellationToken) =>
        SqliteItemDomainStoreSession.ExistingIn(owner)?.ApplyDecidedWritesAsync(cancellationToken)
        ?? ValueTask.CompletedTask;

    private async ValueTask<IReadOnlyList<CreatureState>> ReadRowsAsync(
        CharacterId characterId, int? itemUid, bool currentOnly, CancellationToken cancellationToken)
    {
        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            SELECT held.item_uid, held.item_core, held.creature_field04,
                   held.creature_progress_value, held.creature_field_after_value,
                   held.creature_text, held.creature_mode_flag,
                   held.creature_mode1_field0a, held.creature_mode1_field0b,
                   held.creature_tail_flag
            FROM items held
            JOIN item_slots place ON place.core_uid=held.item_uid
            WHERE place.owner_scope='character' AND place.owner_id=@owner
              AND held.creature_field04 IS NOT NULL
              AND (@uid IS NULL OR held.item_uid=@uid)
              AND (@current=0 OR (place.list_type=3 AND place.slot_index=25));
            """;
        command.Parameters.AddWithValue("@owner", characterId.Value);
        command.Parameters.AddWithValue("@uid", itemUid is null ? DBNull.Value : itemUid.Value);
        command.Parameters.AddWithValue("@current", currentOnly ? 1 : 0);
        var rows = new List<CreatureState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var core = reader.GetFieldValue<byte[]>(1);
            if (core.Length != 82 || core[0] != 5
                || BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1, 4)) <= 0)
                throw new InvalidDataException("Creature detail has no matching creature item kind and template.");
            var name = reader.IsDBNull(5) ? [] : reader.GetFieldValue<byte[]>(5);
            if (name.Length > 4096) throw new InvalidDataException("Creature name is too large.");
            rows.Add(new CreatureState(reader.GetInt32(0),
                BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1, 4)),
                reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                [.. name], reader.GetInt32(6), reader.GetInt32(7),
                reader.GetInt32(8), reader.GetInt32(9)));
        }
        return rows;
    }

    private CreatureState ApplyCondition(CharacterId ownerId, CreatureState value) =>
        this.conditionWrites.TryGetValue((ownerId, value.ItemUid), out var condition)
            ? value with { Stomach = condition.Stomach, ExactStomach = condition.Stomach }
            : value;

    private static CreatureState Copy(CreatureState value) =>
        value with { NameBytes = [.. value.NameBytes] };

    private static byte[] TemplateBytes(int templateId)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, templateId);
        return bytes;
    }
}
