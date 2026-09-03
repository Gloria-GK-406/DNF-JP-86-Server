using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Characters;

/// <summary>
/// Naming several characters at once without building the statement out of their ids.
/// </summary>
/// <remarks>
/// A list of ids cannot be one parameter, and pasting them into the text would make the statement
/// depend on the data. One parameter per id keeps it a statement about a fixed shape, and the
/// shape only changes when the number of characters does.
/// </remarks>
internal static class CharacterIdList
{
    public static string Placeholders(IReadOnlyList<int> characterIds) =>
        string.Join(',', characterIds.Select((_, index) => $"@c{index}"));

    public static void Bind(SqliteParameterCollection parameters, IReadOnlyList<int> characterIds)
    {
        for (var index = 0; index < characterIds.Count; index++)
        {
            parameters.AddWithValue($"@c{index}", characterIds[index]);
        }
    }
}

/// <summary>The <c>characters</c> table.</summary>
internal sealed class SqliteCharacterTable(SqliteDatabase database) : ICharacterTable
{
    /// <summary>
    /// A character's name, however it was stored.
    /// </summary>
    /// <remarks>
    /// Older rows kept only the text form. Reading both and preferring the bytes is what lets a
    /// name that is not valid text survive, which some of them are.
    /// </remarks>
    private const string Name = "COALESCE(name_bytes, CAST(name AS BLOB))";

    /// <summary>The name column, empty when a row has neither form of it.</summary>
    private static byte[] NameOf(SqliteDataReader reader) =>
        reader.IsDBNull(2) ? [] : (byte[])reader.GetValue(2);

    public async ValueTask<IReadOnlyList<CharacterRosterRow>> ReadByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        var rows = await database.ReadAllAsync(
            """
            SELECT character_id,slot_index,level FROM characters
            WHERE account_id=@aid AND delete_flag=0 ORDER BY slot_index,character_id
            """,
            parameters => parameters.AddWithValue("@aid", accountId.Value),
            Roster,
            cancellationToken).ConfigureAwait(false);
        // A fact about the whole roster rather than about a row, so it waits until they are here.
        return rows.Select(row => row.SlotIndex).Distinct().Count() == rows.Count
            ? rows
            : throw new InvalidDataException("Account roster contains duplicate slots.");
    }

    public ValueTask<IReadOnlyList<CharacterSummaryRow>> ReadSummariesByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            $"""
            SELECT character_id, slot_index, {Name}, job, grow_type, level, clone_title_item_id,
                   appearance_blob, created_at
            FROM characters
            WHERE account_id = @aid AND delete_flag = 0
            ORDER BY slot_index, character_id;
            """,
            parameters => parameters.AddWithValue("@aid", accountId.Value),
            reader => new CharacterSummaryRow(
                reader.GetInt32(0), reader.GetInt32(1), NameOf(reader),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
                // Read wide and guarded: the column holds an unsigned item id, so the top half of
                // its range does not fit the signed read, and a character wearing no title has
                // nothing in it at all.
                reader.IsDBNull(6) ? 0L : reader.GetInt64(6),
                // The same blob the entry path reads. The roster screen draws the character, so it
                // needs the parts, not just the name and level.
                EntryBlob.Bounded(reader, 7, EntryBlob.Appearance, "appearance", allowNull: true),
                reader.GetInt32(8)),
            cancellationToken);

    public ValueTask<CharacterSummaryRow?> FindBySlotAsync(
        AccountId accountId, int slotIndex, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            $"""
            SELECT character_id, slot_index, {Name}, job, grow_type, level, created_at
            FROM characters
            WHERE account_id = @aid AND slot_index = @slot AND delete_flag = 0
            LIMIT 1;
            """,
            parameters =>
            {
                parameters.AddWithValue("@aid", accountId.Value);
                parameters.AddWithValue("@slot", slotIndex);
            },
            // Neither the title nor the appearance is selected here: this lookup answers "which
            // character is in that slot", and its callers do not draw anything. Both are empty
            // rather than absent because the row type is shared with the roster query.
            reader => new CharacterSummaryRow(
                reader.GetInt32(0), reader.GetInt32(1), NameOf(reader),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), 0, [],
                reader.GetInt32(6)),
            cancellationToken);

    public async ValueTask<bool> NameIsTakenAsync(
        byte[] name, CancellationToken cancellationToken) =>
        await database.ReadFirstAsync(
            $"SELECT 1 FROM characters WHERE {Name} = @name AND delete_flag = 0 LIMIT 1;",
            parameters => parameters.AddWithValue("@name", name),
            _ => Present,
            cancellationToken).ConfigureAwait(false) is not null;

    public async ValueTask<bool> BelongsToAsync(
        CharacterId characterId, AccountId accountId, CancellationToken cancellationToken) =>
        await database.ReadFirstAsync(
            """
            SELECT 1
            FROM characters
            WHERE character_id = @characterId AND account_id = @accountId
            LIMIT 1;
            """,
            parameters =>
            {
                parameters.AddWithValue("@characterId", characterId.Value);
                parameters.AddWithValue("@accountId", accountId.Value);
            },
            _ => Present,
            cancellationToken).ConfigureAwait(false) is not null;

    public async ValueTask<AccountId?> FindOwnerAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        await database.ReadFirstAsync(
            "SELECT account_id FROM characters WHERE character_id=@cid AND delete_flag=0",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => (object)new AccountId(reader.GetInt32(0)),
            cancellationToken).ConfigureAwait(false) is AccountId owner ? owner : null;

    public ValueTask<CharacterProgressRow?> FindProgressAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            "SELECT level,exp FROM characters WHERE character_id=@cid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterProgressRow(reader.GetInt32(0), reader.GetInt64(1)),
            cancellationToken);

    public ValueTask<CharacterStandingRow?> FindStandingAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            "SELECT job,grow_type,level FROM characters "
            + "WHERE character_id=@cid AND delete_flag=0",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterStandingRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
            cancellationToken);

    public async ValueTask<CharacterId> InsertAsync(
        AccountId accountId,
        string name,
        byte[] nameBytes,
        int job,
        int slotIndex,
        byte[] appearanceBlob,
        CancellationToken cancellationToken)
    {
        var created = await database.WriteReturningAsync(
            """
            INSERT INTO characters (account_id, name, name_bytes, job, grow_type, level, slot_index, created_at, appearance_blob)
            VALUES (@aid, @name, @nameBytes, @job, 0, 1, @slot, unixepoch(), @appearance)
            RETURNING character_id;
            """,
            parameters =>
            {
                parameters.AddWithValue("@aid", accountId.Value);
                parameters.AddWithValue("@name", name);
                parameters.AddWithValue("@nameBytes", nameBytes);
                parameters.AddWithValue("@job", job);
                parameters.AddWithValue("@slot", slotIndex);
                parameters.AddWithValue("@appearance", appearanceBlob);
            },
            reader => (object)new CharacterId(reader.GetInt32(0)),
            cancellationToken).ConfigureAwait(false);
        return created is CharacterId characterId
            ? characterId
            : throw new InvalidOperationException("Character insert did not return an identifier.");
    }

    public ValueTask<int> SetProgressAsync(
        CharacterId characterId, int level, long experience, CancellationToken cancellationToken) =>
        database.ExecuteAsync(
            "UPDATE characters SET level=@level,exp=@exp WHERE character_id=@cid",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@level", level);
                parameters.AddWithValue("@exp", experience);
            },
            cancellationToken);

    public async ValueTask<bool> MarkDeletedAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        await database.ExecuteAsync(
            "UPDATE characters SET delete_flag = 1 WHERE character_id = @cid AND delete_flag = 0;",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            cancellationToken).ConfigureAwait(false) > 0;

    /// <summary>Stands in for a row whose presence is all that was asked.</summary>
    private static readonly object Present = new();

    private static CharacterRosterRow Roster(SqliteDataReader reader)
    {
        var row = new CharacterRosterRow(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        return row.CharacterId > 0 && row.SlotIndex is >= 0 and <= ushort.MaxValue
            && row.Level is >= 0 and <= byte.MaxValue
            ? row
            : throw new InvalidDataException("Account roster state is out of range.");
    }
}

/// <summary>The <c>character_buy_restrict_items</c> table.</summary>
internal sealed class SqliteCharacterBuyRestrictTable(SqliteDatabase database)
    : ICharacterBuyRestrictTable
{
    public ValueTask<IReadOnlyList<CharacterBuyRestrictRow>> ReadByCharactersAsync(
        IReadOnlyList<int> characterIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(characterIds);
        return characterIds.Count == 0
            ? ValueTask.FromResult<IReadOnlyList<CharacterBuyRestrictRow>>([])
            : database.ReadAllAsync(
                "SELECT character_id,sort_order,entry_id,sentinel_or_value,flag "
                + "FROM character_buy_restrict_items "
                + $"WHERE character_id IN ({CharacterIdList.Placeholders(characterIds)}) "
                + "ORDER BY character_id,sort_order",
                parameters => CharacterIdList.Bind(parameters, characterIds),
                Row,
                cancellationToken);
    }

    private static CharacterBuyRestrictRow Row(SqliteDataReader reader)
    {
        var row = new CharacterBuyRestrictRow(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
            reader.GetInt32(3), reader.GetInt32(4));
        return row.SortOrder >= 0
            ? row
            : throw new InvalidDataException("Unknown730 compatibility ordering is invalid.");
    }
}

/// <summary>The <c>character_mercenary_support</c> table.</summary>
internal sealed class SqliteCharacterMercenarySupportTable(SqliteDatabase database)
    : ICharacterMercenarySupportTable
{
    public ValueTask<IReadOnlyList<CharacterMercenarySupportRow>> ReadByOwnersAsync(
        IReadOnlyList<int> ownerCharacterIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerCharacterIds);
        return ownerCharacterIds.Count == 0
            ? ValueTask.FromResult<IReadOnlyList<CharacterMercenarySupportRow>>([])
            : database.ReadAllAsync(
                "SELECT owner_character_id,slot,support_character_id,skill_id,striker_skill_id "
                + "FROM character_mercenary_support "
                + $"WHERE owner_character_id IN ({CharacterIdList.Placeholders(ownerCharacterIds)}) "
                + "ORDER BY owner_character_id,slot",
                parameters => CharacterIdList.Bind(parameters, ownerCharacterIds),
                Row,
                cancellationToken);
    }

    private static CharacterMercenarySupportRow Row(SqliteDataReader reader)
    {
        var row = new CharacterMercenarySupportRow(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
            reader.GetInt32(3), reader.GetInt32(4));
        return row.OwnerCharacterId > 0 && row.Slot is >= 0 and <= byte.MaxValue
            && row.SupportCharacterId > 0 && row.SkillId > 0 && row.StrikerSkillId >= 0
            ? row
            : throw new InvalidDataException("Account relation state is out of range.");
    }
}
