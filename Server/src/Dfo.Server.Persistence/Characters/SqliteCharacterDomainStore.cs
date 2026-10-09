using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Creatures;
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
        var scope = ItemLifecycleScope.In(transaction);
        var database = transaction.Enlist(
            this.StoreIdentity,
            () => new SqliteDatabaseParticipant(this.connectionString));
        return database.Facet(FacetKey, owner => new SqliteCharacterDomainStoreSession(owner, scope));
    }
}

/// <summary>One use case's reads and writes against the characters in the database.</summary>
/// <remarks>
/// Writes are held until the database prepares. Reads consult the held writes first, so the use
/// case reads back what it has just decided without anyone outside seeing it.
/// </remarks>
internal sealed class SqliteCharacterDomainStoreSession : ICharacterDomainStoreSession, ISqliteStoreFacet
{
    private readonly SqliteDatabaseParticipant owner;

    private readonly CreatureSection creatures;

    private readonly Dictionary<OwnedCharacterId, CharacterPositionState> positions = [];
    private readonly Dictionary<OwnedCharacterId, byte> innTowns = [];

    public SqliteCharacterDomainStoreSession(SqliteDatabaseParticipant owner, ItemLifecycleScope scope)
    {
        this.owner = owner;
        this.creatures = new CreatureSection(owner, scope);
    }

    public IDomainStoreSession<OwnedCharacterId, long, StoredCreature> Creatures => this.creatures;

    public bool HasStagedWork => this.positions.Count > 0;

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

    public void WritePosition(OwnedCharacterId characterId, CharacterPositionState position, byte? lastInnTownId = null)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (lastInnTownId == 0) throw new ArgumentOutOfRangeException(nameof(lastInnTownId));
        this.positions[characterId] = position;
        if (lastInnTownId is { } inn) this.innTowns[characterId] = inn;
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        foreach (var (characterId, position) in this.positions)
        {
            await this.FillPositionAsync(characterId, position, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Discard()
    {
        this.positions.Clear();
        this.innTowns.Clear();
    }

    private async ValueTask FillPositionAsync(
        OwnedCharacterId characterId,
        CharacterPositionState position,
        CancellationToken cancellationToken)
    {
        await using var command = await this.owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            UPDATE characters
            SET town_id=@town, area_id=@area, pos_x=@x, pos_y=@y, direction=@direction, area_state=3,
                last_inn_town_id=COALESCE(@inn, last_inn_town_id)
            WHERE character_id=@character AND account_id=@account AND delete_flag=0;
            """;
        command.Parameters.AddWithValue("@town", position.TownId);
        command.Parameters.AddWithValue("@inn", this.innTowns.TryGetValue(characterId, out var inn) ? inn : DBNull.Value);
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

/// <summary>Compatibility view for the entry revival path; all writes join the creature facet.</summary>
internal sealed class CreatureSection(SqliteDatabaseParticipant owner, ItemLifecycleScope scope)
    : IDomainStoreSession<OwnedCharacterId, long, StoredCreature>
{
    private readonly SqliteCreatureDomainStoreSession creatures = SqliteCreatureDomainStoreSession.In(owner, scope);

    public async ValueTask<IReadOnlyList<StoredCreature>> ReadAllAsync(
        OwnedCharacterId place, CancellationToken cancellationToken) =>
        [.. (await this.creatures.ReadAllAsync(place.Character, cancellationToken)
            .ConfigureAwait(false)).Select(creature => new StoredCreature(creature.ItemUid, creature.Stomach))];

    public async ValueTask<StoredCreature?> ReadAsync(
        OwnedCharacterId place, long key, CancellationToken cancellationToken)
    {
        if (key is <= 0 or > int.MaxValue) return null;
        var creature = await this.creatures.ReadAsync(place.Character, (int)key, cancellationToken)
            .ConfigureAwait(false);
        return creature is null ? null : new StoredCreature(key, creature.Stomach);
    }

    public void Write(OwnedCharacterId place, StoredCreature value) =>
        this.creatures.WriteCondition(place, value.CreatureKey, value.Field04, value.OnlyWhenAtMostZero);

    public void Clear(OwnedCharacterId place, long key) =>
        throw new NotSupportedException("Clearing condition would delete creature state.");
}
