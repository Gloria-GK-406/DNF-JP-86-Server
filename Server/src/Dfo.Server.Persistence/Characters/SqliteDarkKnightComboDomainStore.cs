using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Characters;

/// <summary>
/// The store whose combo pages are rows in the character's own database.
/// </summary>
/// <remarks>
/// Uses the table the character repositories already create; it defines none of its own.
///
/// Its identity is the database, the same identity the character, item and quest stores answer, so
/// a use case that changes a combo page and anything else kept there joins one participant rather
/// than two -- which is the only way both can be settled together, since a transaction admits one
/// participant whose commit can still fail.
///
/// Its reads are the entry load's reads: it is handed <see cref="ICharacterComboPageTable"/> and
/// asks that, rather than issuing a select of its own beside it. Two statements over one table are
/// two readers that can drift, and the quest store next door has to document at length what its
/// own second reader does and does not agree with. There is nothing here for that document to
/// say, because there is one statement. Inside a use case the gateway reaches the same participant
/// this store enlists, so the read is the use case's read; outside one it is a read of its own,
/// which is what a read outside a use case is anyway.
/// </remarks>
internal sealed class SqliteDarkKnightComboDomainStore : IDarkKnightComboDomainStore
{
    /// <summary>What the database registers this store's session under.</summary>
    private static readonly object FacetKey = typeof(SqliteDarkKnightComboDomainStoreSession);

    private readonly string connectionString;

    private readonly ICharacterComboPageTable pages;

    public SqliteDarkKnightComboDomainStore(string databasePath, ICharacterComboPageTable pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var fullPath = Path.GetFullPath(databasePath);
        this.connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        this.pages = pages;
    }

    public object StoreIdentity => this.connectionString;

    public bool IsDurable => true;

    public IDarkKnightComboDomainStoreSession SessionIn(IDomainTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var database = transaction.Enlist(
            this.StoreIdentity,
            () => new SqliteDatabaseParticipant(this.connectionString));
        return database.Facet(
            FacetKey, owner => new SqliteDarkKnightComboDomainStoreSession(owner, this.pages));
    }
}

/// <summary>One use case's reads and writes against the combo pages in the character database.</summary>
/// <remarks>
/// Writes are held until the database prepares. Reads consult the held writes first, so the use
/// case reads back what it has just decided without anyone outside seeing it.
///
/// Whether a character belongs to the account it was named under is checked once per character
/// while flushing, before any of that character's rows are touched. That is the store refusing an
/// address it cannot place, not a rule about combo pages: the account is half of the address.
/// </remarks>
internal sealed class SqliteDarkKnightComboDomainStoreSession
    : IDarkKnightComboDomainStoreSession, ISqliteStoreFacet
{
    private readonly SqliteDatabaseParticipant owner;

    private readonly ComboPageSection pages;

    public SqliteDarkKnightComboDomainStoreSession(
        SqliteDatabaseParticipant owner, ICharacterComboPageTable gateway)
    {
        this.owner = owner;
        this.pages = new ComboPageSection(owner, gateway);
    }

    public bool HasStagedWork => this.pages.HasStagedWork;

    public ValueTask<IReadOnlyList<DarkKnightComboEntryState>> ReadAllAsync(
        OwnedCharacterId place, CancellationToken cancellationToken) =>
        this.pages.ReadAllAsync(place, cancellationToken);

    public ValueTask<DarkKnightComboEntryState?> ReadAsync(
        OwnedCharacterId place, byte pageIndex, CancellationToken cancellationToken) =>
        this.pages.ReadAsync(place, pageIndex, cancellationToken);

    public void Write(OwnedCharacterId place, DarkKnightComboEntryState value) =>
        this.pages.Write(place, value);

    public void Clear(OwnedCharacterId place, byte pageIndex) => this.pages.Clear(place, pageIndex);

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        foreach (var characterId in this.pages.Touched)
        {
            await this.RefuseUnlessOwnedAsync(characterId, cancellationToken).ConfigureAwait(false);
        }

        await this.pages.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Discard() => this.pages.Discard();

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

/// <summary>The combo pages one character has.</summary>
/// <remarks>
/// The rows are in <c>character_dark_knight_combo_skill_pages</c>, one per page, and the reading
/// of them is the entry load's own gateway rather than a second statement over the same table.
/// </remarks>
internal sealed class ComboPageSection(
    SqliteDatabaseParticipant owner, ICharacterComboPageTable gateway)
    : SqliteOwnedCharacterSection<byte, DarkKnightComboEntryState>(owner)
{
    /// <summary>The highest page number a character can have.</summary>
    /// <remarks>
    /// Two pages, which is what the entry load will carry: it refuses a row outside them outright.
    /// A row written past the end would therefore not be a wrong answer but an unusable character
    /// -- every later entry would fail on it, and nothing would say which row was to blame.
    ///
    /// Refused where it is asked for rather than while flushing, for the same reason a cleared
    /// quest's number is: by then other kinds of state have written their statements, and a
    /// refusal raised there would take them down with it.
    /// </remarks>
    private const byte HighestPage = 1;

    protected override byte KeyOf(DarkKnightComboEntryState value) => value.PageIndex;

    public override void Write(OwnedCharacterId place, DarkKnightComboEntryState value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.PageIndex > HighestPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.PageIndex,
                "A combo page the entry load will not carry is one no character could enter with.");
        }

        // The bound the read of these rows already applies: ReadRowsAsync below asks the gateway,
        // and the gateway refuses a body longer than EntryBlob.Combo where it reads the column. A
        // longer body would therefore settle as a row that every later read of this character's
        // pages throws on -- a page written and then not readable, rather than a page written
        // wrongly. Refused here for the same reason the page number is: raised while flushing, it
        // would take down the statements other kinds of state have already written.
        if (value.EncodedBody.Length > EntryBlob.Combo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.EncodedBody.Length,
                "A combo page body the read of these rows will not return is one no character could enter with.");
        }

        base.Write(place, value);
    }

    protected override async ValueTask<IReadOnlyList<DarkKnightComboEntryState>> ReadRowsAsync(
        OwnedCharacterId place, CancellationToken cancellationToken)
    {
        var rows = await gateway.ReadAsync(place.Character, cancellationToken).ConfigureAwait(false);
        var pages = new List<DarkKnightComboEntryState>(rows.Count);
        foreach (var row in rows)
        {
            // A page number outside what the entry load will carry is a row it refuses outright,
            // so answering with it here would hand back a page entry would then reject.
            if (row.PageIndex is < 0 or > HighestPage)
            {
                throw new InvalidDataException("Dark Knight combo page is out of range.");
            }

            pages.Add(new DarkKnightComboEntryState(checked((byte)row.PageIndex), row.Body));
        }

        return pages;
    }

    protected override async ValueTask FillAsync(
        OwnedCharacterId place, DarkKnightComboEntryState value, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            INSERT INTO character_dark_knight_combo_skill_pages(character_id, page_index, body)
            VALUES(@character, @page, @body)
            ON CONFLICT(character_id, page_index) DO UPDATE SET body=@body;
            """;
        command.Parameters.AddWithValue("@character", place.Character.Value);
        command.Parameters.AddWithValue("@page", value.PageIndex);
        command.Parameters.AddWithValue("@body", value.EncodedBody);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask EmptyAsync(
        OwnedCharacterId place, byte pageIndex, CancellationToken cancellationToken)
    {
        await using var command = await this.Owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText =
            "DELETE FROM character_dark_knight_combo_skill_pages "
            + "WHERE character_id=@character AND page_index=@page;";
        command.Parameters.AddWithValue("@character", place.Character.Value);
        command.Parameters.AddWithValue("@page", pageIndex);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
