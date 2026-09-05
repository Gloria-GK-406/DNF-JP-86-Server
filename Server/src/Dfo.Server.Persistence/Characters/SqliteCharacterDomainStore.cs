using System.Diagnostics;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Characters;

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
internal sealed class SqliteCharacterDomainStore : ICharacterDomainStore
{
    /// <summary>What the database registers this store's session under.</summary>
    private static readonly object FacetKey = typeof(SqliteCharacterDomainStoreSession);

    private readonly string connectionString;

    public SqliteCharacterDomainStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        this.connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
    }

    public object StoreIdentity => this.connectionString;

    public bool IsDurable => true;

    public ICharacterDomainStoreSession SessionIn(IDomainTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var database = transaction.Enlist(
            this.StoreIdentity,
            () => new SqliteDatabaseParticipant(this.connectionString));
        return database.Facet(FacetKey, owner => new SqliteCharacterDomainStoreSession(owner));
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
internal sealed class SqliteCharacterDomainStoreSession : ICharacterDomainStoreSession, ISqliteStoreFacet
{
    private readonly SqliteDatabaseParticipant owner;

    private readonly CreatureSection creatures;

    private readonly Dictionary<OwnedCharacterId, CharacterPositionState> positions = [];

    public SqliteCharacterDomainStoreSession(SqliteDatabaseParticipant owner)
    {
        this.owner = owner;
        this.creatures = new CreatureSection(owner);
    }

    public IDomainStoreSession<OwnedCharacterId, long, StoredCreature> Creatures => this.creatures;

    public bool HasStagedWork => this.creatures.HasStagedWork || this.positions.Count > 0;

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

        await this.creatures.FlushAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (characterId, position) in this.positions)
        {
            await this.FillPositionAsync(characterId, position, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Discard()
    {
        this.creatures.Discard();
        this.positions.Clear();
    }

    /// <remarks>
    /// Positions are absent on purpose. Their statement carries the pairing in its own predicate
    /// and refuses in its own words, exactly as it always has; asking here as well would replace
    /// that refusal with this one and change what a caller is told.
    /// </remarks>
    private IEnumerable<OwnedCharacterId> Touched() => this.creatures.Touched.Distinct();

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

/// <summary>The condition each of a character's creatures is in.</summary>
/// <remarks>
/// This section addresses one column of a creature's world row rather than the whole of it. The
/// rest of the row -- its core, its text, its mode, where it is standing -- takes part in no use
/// case that writes here, and carrying it through a read and a write only to put it back unchanged
/// would widen what can go wrong without widening what can be done.
///
/// The rows are the creature rows of <c>items</c>, and the key is the world identity. They used to
/// be rows of <c>character_creatures</c> under a key of their own; a creature is one row now, so
/// there is no second row to keep in step and no moment at which the two could disagree.
///
/// Whether a row is a creature at all is read off the same column this writes, so a write is held
/// to rows that already are creatures. Setting it on a row that is not one would not correct
/// anything -- it would make that row answer as a creature to every read that asks.
///
/// Whose creature it is comes from the position holding it. A world row says what a thing is and
/// nothing about whose it is, so both the read and the write ask <c>item_slots</c> that.
/// </remarks>
internal sealed class CreatureSection(SqliteDatabaseParticipant owner)
    : SqliteOwnedCharacterSection<long, StoredCreature>(owner)
{
    protected override long KeyOf(StoredCreature value) => value.CreatureKey;

    protected override async ValueTask<IReadOnlyList<StoredCreature>> ReadRowsAsync(
        OwnedCharacterId place, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        // A row with no condition recorded is one this section does not hold. Reading it as
        // nothing would say the creature is in a condition it was never put in, and a caller
        // acting on that would then fail against a guard the row cannot satisfy.
        command.CommandText = """
            SELECT held.item_uid, held.creature_field04 FROM items held
            WHERE held.creature_field04 IS NOT NULL
              AND EXISTS (
                  SELECT 1 FROM item_slots place
                  WHERE place.owner_scope = 'character' AND place.owner_id = @character
                    AND place.core_uid = held.item_uid);
            """;
        command.Parameters.AddWithValue("@character", place.Character.Value);
        var rows = new List<StoredCreature>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
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
                UPDATE items SET creature_field04=@condition
                WHERE item_uid=@key AND creature_field04<=0
                  AND EXISTS (
                      SELECT 1 FROM item_slots place
                      WHERE place.owner_scope='character' AND place.owner_id=@character
                        AND place.core_uid=@key);
                """
            : """
                UPDATE items SET creature_field04=@condition
                WHERE item_uid=@key AND creature_field04 IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM item_slots place
                      WHERE place.owner_scope='character' AND place.owner_id=@character
                        AND place.core_uid=@key);
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
    /// A creature's condition is a column of a world row this store does not otherwise own, so
    /// there is nothing here to empty. Clearing it would mean deleting the creature itself, which
    /// is not what this section addresses.
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
