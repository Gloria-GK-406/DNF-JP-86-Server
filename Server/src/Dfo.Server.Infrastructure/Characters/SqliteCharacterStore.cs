using System.Diagnostics;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Characters;

/// <summary>
/// The store whose characters are rows in the character database.
/// </summary>
/// <remarks>
/// Uses the tables the character repositories already create; it defines none of its own.
///
/// Its identity is the database, the same identity the item store answers, so a use case that
/// changes a character and the items it owns joins one participant rather than two -- which is the
/// only way both can be settled together, since a transaction admits one participant whose commit
/// can still fail.
/// </remarks>
internal sealed class SqliteCharacterStore : ICharacterStore
{
    /// <summary>What the database registers this store's session under.</summary>
    private static readonly object FacetKey = typeof(SqliteCharacterStoreSession);

    private readonly string connectionString;

    public SqliteCharacterStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        this.connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
    }

    public object StoreIdentity => this.connectionString;

    public bool IsDurable => true;

    public ICharacterStoreSession SessionIn(IDomainTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var database = transaction.Enlist(
            this.StoreIdentity,
            () => new SqliteDatabaseParticipant(this.connectionString));
        return database.Facet(FacetKey, owner => new SqliteCharacterStoreSession(owner));
    }
}

/// <summary>One use case's reads and writes against the characters in the database.</summary>
/// <remarks>
/// Writes are held until the database prepares. Reads consult the held writes first, so the use
/// case reads back what it has just decided without anyone outside seeing it.
///
/// Whether a character belongs to the account it was named under is checked once per character
/// while flushing, before any of that character's rows are touched. That is the store refusing an
/// address it cannot place, not a rule about characters: the account is half of the address.
/// </remarks>
internal sealed class SqliteCharacterStoreSession : ICharacterStoreSession, ISqliteStoreFacet
{
    private readonly SqliteDatabaseParticipant owner;

    private readonly QuestSection quests;

    private readonly CreatureSection creatures;

    private readonly Dictionary<OwnedCharacterId, CharacterPositionState> positions = [];

    public SqliteCharacterStoreSession(SqliteDatabaseParticipant owner)
    {
        this.owner = owner;
        this.quests = new QuestSection(owner);
        this.creatures = new CreatureSection(owner);
    }

    public IStoreSession<OwnedCharacterId, short, StoredActiveQuest> Quests => this.quests;

    public IStoreSession<OwnedCharacterId, long, StoredCreature> Creatures => this.creatures;

    public bool HasStagedWork =>
        this.quests.HasStagedWork || this.creatures.HasStagedWork || this.positions.Count > 0;

    public async ValueTask<bool> HoldsAsync(
        OwnedCharacterId characterId, CancellationToken cancellationToken)
    {
        await using var command = await this.owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText =
            "SELECT 1 FROM characters WHERE character_id=@character AND account_id=@account AND delete_flag=0;";
        command.Parameters.AddWithValue("@character", characterId.Character.Value);
        command.Parameters.AddWithValue("@account", characterId.Account.Value);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    public async ValueTask<CharacterPositionState?> ReadPositionAsync(
        OwnedCharacterId characterId, CancellationToken cancellationToken)
    {
        if (this.positions.TryGetValue(characterId, out var staged))
        {
            return staged;
        }

        await using var command = await this.owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            SELECT town_id, area_id, pos_x, pos_y, direction
            FROM characters
            WHERE character_id = @character AND account_id = @account AND delete_flag = 0;
            """;
        command.Parameters.AddWithValue("@character", characterId.Character.Value);
        command.Parameters.AddWithValue("@account", characterId.Account.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new CharacterPositionState(
                checked((byte)reader.GetInt32(0)), checked((byte)reader.GetInt32(1)),
                checked((short)reader.GetInt32(2)), checked((short)reader.GetInt32(3)),
                checked((byte)reader.GetInt32(4)))
            : null;
    }

    public void WritePosition(OwnedCharacterId characterId, CharacterPositionState position)
    {
        ArgumentNullException.ThrowIfNull(position);
        this.positions[characterId] = position;
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        foreach (var characterId in this.Touched())
        {
            await this.RefuseUnlessOwnedAsync(characterId, cancellationToken).ConfigureAwait(false);
        }

        await this.quests.FlushAsync(cancellationToken).ConfigureAwait(false);
        await this.creatures.FlushAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (characterId, position) in this.positions)
        {
            await this.FillPositionAsync(characterId, position, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Discard()
    {
        this.quests.Discard();
        this.creatures.Discard();
        this.positions.Clear();
    }

    /// <remarks>
    /// Positions are absent on purpose. Their statement carries the pairing in its own predicate
    /// and refuses in its own words, exactly as it always has; asking here as well would replace
    /// that refusal with this one and change what a caller is told.
    /// </remarks>
    private IEnumerable<OwnedCharacterId> Touched() =>
        this.quests.Touched.Concat(this.creatures.Touched).Distinct();

    /// <remarks>
    /// The same pairing the section writes are addressed by. A character that is not this
    /// account's, or has been deleted, is not an address this store can place.
    /// </remarks>
    private async ValueTask RefuseUnlessOwnedAsync(
        OwnedCharacterId characterId, CancellationToken cancellationToken)
    {
        await using var command = await this.owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText =
            "SELECT 1 FROM characters WHERE character_id=@character AND account_id=@account AND delete_flag=0;";
        command.Parameters.AddWithValue("@character", characterId.Character.Value);
        command.Parameters.AddWithValue("@account", characterId.Account.Value);
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            throw new InvalidDataException("Entry mutation character ownership does not match.");
        }
    }

    private async ValueTask FillPositionAsync(
        OwnedCharacterId characterId,
        CharacterPositionState position,
        CancellationToken cancellationToken)
    {
        await using var command = await this.owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            UPDATE characters
            SET town_id=@town, area_id=@area, pos_x=@x, pos_y=@y, direction=@direction, area_state=3
            WHERE character_id=@character AND account_id=@account AND delete_flag=0;
            """;
        command.Parameters.AddWithValue("@town", position.TownId);
        command.Parameters.AddWithValue("@area", position.AreaId);
        command.Parameters.AddWithValue("@x", position.X);
        command.Parameters.AddWithValue("@y", position.Y);
        command.Parameters.AddWithValue("@direction", position.Direction);
        command.Parameters.AddWithValue("@character", characterId.Character.Value);
        command.Parameters.AddWithValue("@account", characterId.Account.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidDataException("Entry position owner is unavailable.");
        }
    }
}

/// <summary>One of a character's sections that holds many rows, staged and flushed as a batch.</summary>
/// <remarks>
/// The staging, the overlay a read applies to what the database says, and letting go afterwards
/// are the same for every such section; only the rows and the statements differ.
/// </remarks>
internal abstract class SqliteCharacterSection<TKey, TValue>(SqliteDatabaseParticipant owner)
    : IStoreSession<OwnedCharacterId, TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly Dictionary<OwnedCharacterId, Dictionary<TKey, TValue?>> staged = [];

    public bool HasStagedWork => this.staged.Count > 0;

    public IEnumerable<OwnedCharacterId> Touched => this.staged.Keys;

    protected SqliteDatabaseParticipant Owner { get; } = owner;

    public async ValueTask<IReadOnlyList<TValue>> ReadAllAsync(
        OwnedCharacterId place, CancellationToken cancellationToken)
    {
        var rows = new SortedDictionary<TKey, TValue>(
            Comparer<TKey>.Default);
        foreach (var value in await this.ReadRowsAsync(place, cancellationToken).ConfigureAwait(false))
        {
            rows[this.KeyOf(value)] = value;
        }

        if (this.staged.TryGetValue(place, out var held))
        {
            foreach (var (key, value) in held)
            {
                if (value is null)
                {
                    rows.Remove(key);
                }
                else
                {
                    rows[key] = value;
                }
            }
        }

        return [.. rows.Values];
    }

    public async ValueTask<TValue?> ReadAsync(
        OwnedCharacterId place, TKey key, CancellationToken cancellationToken)
    {
        if (this.staged.TryGetValue(place, out var held) && held.TryGetValue(key, out var value))
        {
            return value;
        }

        foreach (var row in await this.ReadRowsAsync(place, cancellationToken).ConfigureAwait(false))
        {
            if (this.KeyOf(row).Equals(key))
            {
                return row;
            }
        }

        return null;
    }

    public void Write(OwnedCharacterId place, TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        this.Staged(place)[this.KeyOf(value)] = value;
    }

    public virtual void Clear(OwnedCharacterId place, TKey key) => this.Staged(place)[key] = null;

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        foreach (var (place, rows) in this.staged)
        {
            foreach (var (key, value) in rows)
            {
                if (value is null)
                {
                    await this.EmptyAsync(place, key, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await this.FillAsync(place, value, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    public void Discard() => this.staged.Clear();

    protected abstract TKey KeyOf(TValue value);

    protected abstract ValueTask<IReadOnlyList<TValue>> ReadRowsAsync(
        OwnedCharacterId place, CancellationToken cancellationToken);

    protected abstract ValueTask FillAsync(
        OwnedCharacterId place, TValue value, CancellationToken cancellationToken);

    protected abstract ValueTask EmptyAsync(
        OwnedCharacterId place, TKey key, CancellationToken cancellationToken);

    private Dictionary<TKey, TValue?> Staged(OwnedCharacterId place)
    {
        if (!this.staged.TryGetValue(place, out var rows))
        {
            rows = [];
            this.staged.Add(place, rows);
        }

        return rows;
    }
}

/// <summary>The quests a character is carrying.</summary>
internal sealed class QuestSection(SqliteDatabaseParticipant owner)
    : SqliteCharacterSection<short, StoredActiveQuest>(owner)
{
    protected override short KeyOf(StoredActiveQuest value) => value.Slot;

    protected override async ValueTask<IReadOnlyList<StoredActiveQuest>> ReadRowsAsync(
        OwnedCharacterId place, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText =
            "SELECT slot, quest_id, trigger_value FROM character_active_quests WHERE character_id=@character;";
        command.Parameters.AddWithValue("@character", place.Character.Value);
        var rows = new List<StoredActiveQuest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new StoredActiveQuest(
                checked((short)reader.GetInt32(0)), reader.GetInt32(1), reader.GetInt32(2)));
        }

        return rows;
    }

    /// <remarks>
    /// When the caller said what they last saw, the statement carries it, and a row that has since
    /// moved fails rather than being overwritten. That guard is what stands between an entry
    /// snapshot and the row as it is at the moment of writing.
    /// </remarks>
    protected override async ValueTask FillAsync(
        OwnedCharacterId place, StoredActiveQuest value, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = value.ExpectedTriggerValue is null
            ? """
                INSERT INTO character_active_quests(character_id, slot, quest_id, trigger_value)
                VALUES(@character, @slot, @quest, @trigger)
                ON CONFLICT(character_id, slot) DO UPDATE SET quest_id=@quest, trigger_value=@trigger;
                """
            : """
                UPDATE character_active_quests SET trigger_value=@trigger
                WHERE character_id=@character AND slot=@slot AND quest_id=@quest
                  AND trigger_value=@expected;
                """;
        command.Parameters.AddWithValue("@character", place.Character.Value);
        command.Parameters.AddWithValue("@slot", value.Slot);
        command.Parameters.AddWithValue("@quest", value.QuestId);
        command.Parameters.AddWithValue("@trigger", value.TriggerValue);
        if (value.ExpectedTriggerValue is { } expected)
        {
            command.Parameters.AddWithValue("@expected", expected);
        }

        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidDataException($"Seeking quest {value.QuestId} changed concurrently.");
        }
    }

    protected override async ValueTask EmptyAsync(
        OwnedCharacterId place, short key, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText =
            "DELETE FROM character_active_quests WHERE character_id=@character AND slot=@slot;";
        command.Parameters.AddWithValue("@character", place.Character.Value);
        command.Parameters.AddWithValue("@slot", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>The condition each of a character's creatures is in.</summary>
/// <remarks>
/// This section addresses one field of a creature's row rather than the whole of it. The rest of
/// the row -- its ordering, its text, its mode -- takes part in no use case that writes here, and
/// carrying it through a read and a write only to put it back unchanged would widen what can go
/// wrong without widening what can be done.
/// </remarks>
internal sealed class CreatureSection(SqliteDatabaseParticipant owner)
    : SqliteCharacterSection<long, StoredCreature>(owner)
{
    protected override long KeyOf(StoredCreature value) => value.CreatureKey;

    protected override async ValueTask<IReadOnlyList<StoredCreature>> ReadRowsAsync(
        OwnedCharacterId place, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText =
            "SELECT creature_key, field04 FROM character_creatures WHERE character_id=@character;";
        command.Parameters.AddWithValue("@character", place.Character.Value);
        var rows = new List<StoredCreature>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // A row with no condition recorded is one this section does not hold. Reading it as
            // nothing would say the creature is in a condition it was never put in, and a caller
            // acting on that would then fail against a guard the row cannot satisfy.
            if (reader.IsDBNull(1)) continue;
            rows.Add(new StoredCreature(reader.GetInt64(0), reader.GetInt32(1)));
        }

        return rows;
    }

    protected override async ValueTask FillAsync(
        OwnedCharacterId place, StoredCreature value, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = value.OnlyWhenAtMostZero
            ? """
                UPDATE character_creatures SET field04=@condition
                WHERE character_id=@character AND creature_key=@key AND field04<=0;
                """
            : """
                UPDATE character_creatures SET field04=@condition
                WHERE character_id=@character AND creature_key=@key;
                """;
        command.Parameters.AddWithValue("@condition", value.Field04);
        command.Parameters.AddWithValue("@character", place.Character.Value);
        command.Parameters.AddWithValue("@key", value.CreatureKey);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidDataException("Equipped creature revival changed concurrently.");
        }
    }

    /// <remarks>
    /// A creature's condition is a field of a row this store does not otherwise own, so there is
    /// nothing here to empty. Clearing it would mean deleting the creature, which is not what this
    /// section addresses.
    ///
    /// Refused where it is asked for rather than while flushing: by then other kinds of state have
    /// already written their statements, and a refusal raised then would take them down with it.
    /// </remarks>
    public override void Clear(OwnedCharacterId place, long key) =>
        throw new NotSupportedException(
            "A creature's condition cannot be cleared; it is a field of a row this section does not own.");

    protected override ValueTask EmptyAsync(
        OwnedCharacterId place, long key, CancellationToken cancellationToken) =>
        throw new UnreachableException("A creature's condition is refused before it can be staged.");
}
