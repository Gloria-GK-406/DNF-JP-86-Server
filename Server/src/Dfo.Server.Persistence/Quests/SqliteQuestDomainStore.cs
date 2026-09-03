using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Quests;

/// <summary>
/// The store whose quests are rows in the character's own database.
/// </summary>
/// <remarks>
/// Uses the tables the character repositories already create; it defines none of its own.
///
/// Its identity is the database, the same identity the character and item stores answer, so a use
/// case that takes a quest on and hands over what the quest gives joins one participant rather
/// than two -- which is the only way both can be settled together, since a transaction admits one
/// participant whose commit can still fail.
/// </remarks>
internal sealed class SqliteQuestDomainStore : IQuestDomainStore
{
    /// <summary>What the database registers this store's session under.</summary>
    private static readonly object FacetKey = typeof(SqliteQuestDomainStoreSession);

    private readonly string connectionString;

    public SqliteQuestDomainStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        this.connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
    }

    public object StoreIdentity => this.connectionString;

    public bool IsDurable => true;

    public IQuestDomainStoreSession SessionIn(IDomainTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var database = transaction.Enlist(
            this.StoreIdentity,
            () => new SqliteDatabaseParticipant(this.connectionString));
        return database.Facet(FacetKey, owner => new SqliteQuestDomainStoreSession(owner));
    }
}

/// <summary>One use case's reads and writes against the quests in the character database.</summary>
/// <remarks>
/// Writes are held until the database prepares. Reads consult the held writes first, so the use
/// case reads back what it has just decided without anyone outside seeing it.
///
/// Whether a character belongs to the account it was named under is checked once per character
/// while flushing, before any of that character's rows are touched. That is the store refusing an
/// address it cannot place, not a rule about quests: the account is half of the address.
/// </remarks>
internal sealed class SqliteQuestDomainStoreSession : IQuestDomainStoreSession, ISqliteStoreFacet
{
    private readonly SqliteDatabaseParticipant owner;

    private readonly CarriedQuestSection carried;

    private readonly ClearedQuestSection cleared;

    private readonly TrackedQuestSection tracked;

    public SqliteQuestDomainStoreSession(SqliteDatabaseParticipant owner)
    {
        this.owner = owner;
        this.carried = new CarriedQuestSection(owner);
        this.cleared = new ClearedQuestSection(owner);
        this.tracked = new TrackedQuestSection(owner);
    }

    public IDomainStoreSession<OwnedCharacterId, short, StoredActiveQuest> Carried => this.carried;

    public IDomainStoreSession<OwnedCharacterId, int, StoredClearedQuest> Cleared => this.cleared;

    public IDomainStoreSession<OwnedCharacterId, short, StoredTrackedQuest> Tracked => this.tracked;

    public bool HasStagedWork =>
        this.carried.HasStagedWork || this.cleared.HasStagedWork || this.tracked.HasStagedWork;

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        foreach (var characterId in this.Touched())
        {
            await this.RefuseUnlessOwnedAsync(characterId, cancellationToken).ConfigureAwait(false);
        }

        await this.carried.FlushAsync(cancellationToken).ConfigureAwait(false);
        await this.cleared.FlushAsync(cancellationToken).ConfigureAwait(false);
        await this.tracked.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Discard()
    {
        this.carried.Discard();
        this.cleared.Discard();
        this.tracked.Discard();
    }

    private IEnumerable<OwnedCharacterId> Touched() =>
        this.carried.Touched.Concat(this.cleared.Touched).Concat(this.tracked.Touched).Distinct();

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
}

/// <summary>The quests a character is carrying.</summary>
internal sealed class CarriedQuestSection(SqliteDatabaseParticipant owner)
    : SqliteOwnedCharacterSection<short, StoredActiveQuest>(owner)
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

/// <summary>The quests a character has finished.</summary>
/// <remarks>
/// The rows are in <c>character_invisible_flags</c>, whose <c>slot_index</c> is a quest number
/// rather than a position: the original server indexed that flag array by quest, and the entry
/// projection still reads every non-zero one of them as a cleared quest. The column names are that
/// server's; what they mean is this.
/// </remarks>
internal sealed class ClearedQuestSection(SqliteDatabaseParticipant owner)
    : SqliteOwnedCharacterSection<int, StoredClearedQuest>(owner)
{
    /// <summary>The quest numbers this table can hold.</summary>
    /// <remarks>
    /// The entry projection reads these rows into a flag array of thirty thousand and refuses a
    /// row outside it outright. A row written past the end would therefore not be a wrong answer
    /// but an unusable character: every later entry would fail on it, and nothing would say which
    /// row was to blame.
    ///
    /// Refused where it is asked for rather than while flushing, for the same reason a creature's
    /// condition is: by then other kinds of state have written their statements, and a refusal
    /// raised there would take them down with it.
    /// </remarks>
    private const int QuestNumberCeiling = 30_000;

    protected override int KeyOf(StoredClearedQuest value) => value.QuestId;

    public override void Write(OwnedCharacterId place, StoredClearedQuest value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.QuestId is < 0 or >= QuestNumberCeiling)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.QuestId,
                "A cleared quest outside the flag array is one no character could enter with.");
        }

        base.Write(place, value);
    }

    protected override async ValueTask<IReadOnlyList<StoredClearedQuest>> ReadRowsAsync(
        OwnedCharacterId place, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText =
            "SELECT slot_index, flag_value FROM character_invisible_flags WHERE character_id=@character;";
        command.Parameters.AddWithValue("@character", place.Character.Value);
        var rows = new List<StoredClearedQuest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // A mark outside a byte is a row the entry projection refuses outright, so reading it
            // as anything here would invent a value entry would then reject.
            var mark = reader.GetInt32(1);
            if (mark is < 0 or > byte.MaxValue)
            {
                throw new InvalidDataException("Quest clear flag state is out of range.");
            }

            rows.Add(new StoredClearedQuest(reader.GetInt32(0), checked((byte)mark)));
        }

        return rows;
    }

    protected override async ValueTask FillAsync(
        OwnedCharacterId place, StoredClearedQuest value, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            INSERT INTO character_invisible_flags(character_id, slot_index, flag_value)
            VALUES(@character, @quest, @mark)
            ON CONFLICT(character_id, slot_index) DO UPDATE SET flag_value=@mark;
            """;
        command.Parameters.AddWithValue("@character", place.Character.Value);
        command.Parameters.AddWithValue("@quest", value.QuestId);
        command.Parameters.AddWithValue("@mark", value.Mark);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask EmptyAsync(
        OwnedCharacterId place, int key, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText =
            "DELETE FROM character_invisible_flags WHERE character_id=@character AND slot_index=@quest;";
        command.Parameters.AddWithValue("@character", place.Character.Value);
        command.Parameters.AddWithValue("@quest", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Which quests the character asked to be told about.</summary>
internal sealed class TrackedQuestSection(SqliteDatabaseParticipant owner)
    : SqliteOwnedCharacterSection<short, StoredTrackedQuest>(owner)
{
    protected override short KeyOf(StoredTrackedQuest value) => value.SlotIndex;

    protected override async ValueTask<IReadOnlyList<StoredTrackedQuest>> ReadRowsAsync(
        OwnedCharacterId place, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText =
            "SELECT slot_index, quest_id FROM character_quest_notify_selections WHERE character_id=@character;";
        command.Parameters.AddWithValue("@character", place.Character.Value);
        var rows = new List<StoredTrackedQuest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new StoredTrackedQuest(
                checked((short)reader.GetInt32(0)), reader.GetInt32(1)));
        }

        return rows;
    }

    protected override async ValueTask FillAsync(
        OwnedCharacterId place, StoredTrackedQuest value, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            INSERT INTO character_quest_notify_selections(character_id, slot_index, quest_id)
            VALUES(@character, @slot, @quest)
            ON CONFLICT(character_id, slot_index) DO UPDATE SET quest_id=@quest;
            """;
        command.Parameters.AddWithValue("@character", place.Character.Value);
        command.Parameters.AddWithValue("@slot", value.SlotIndex);
        command.Parameters.AddWithValue("@quest", value.QuestId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask EmptyAsync(
        OwnedCharacterId place, short key, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText =
            "DELETE FROM character_quest_notify_selections WHERE character_id=@character AND slot_index=@slot;";
        command.Parameters.AddWithValue("@character", place.Character.Value);
        command.Parameters.AddWithValue("@slot", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
