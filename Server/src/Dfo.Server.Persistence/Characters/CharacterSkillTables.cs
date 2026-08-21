using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

/// <summary>The <c>character_skills</c> table.</summary>
/// <remarks>
/// Both sets of pages are rows of this table, told apart by <c>mode</c>. Every statement names the
/// mode it means -- there is no statement here that works on both, because no caller wants both:
/// spending a skill point is about the ordinary pages and seeding a duel channel is about the duel
/// pages, and a statement that served both would hand one of them rows it never asked for.
///
/// The column that says which is not a discriminator in the sense the red line forbids. Every other
/// column means exactly the same thing in either mode; <c>mode</c> selects a set of rows, not a
/// reading of its neighbours.
/// </remarks>
internal sealed class SqliteCharacterSkillTable(SqliteDatabase database) : ICharacterSkillTable
{
    /// <summary>How big a skill's extra values may be before the row is refused.</summary>
    /// <remarks>
    /// It was the ordinary pages' bound and is now both, because it is one column. A duel row past
    /// it cannot be anything the seeding path wrote, for the same reason an ordinary one cannot.
    /// </remarks>
    private const int ExtraValuesBound = 1024;

    public ValueTask<IReadOnlyList<CharacterSkillLevelRow>> ReadLevelsAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT page_index,slot,skill_id,level FROM character_skills "
            + "WHERE character_id=@cid AND mode='pve'",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterSkillLevelRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
            cancellationToken);

    public ValueTask<IReadOnlyList<CharacterSkillEntryRow>> ReadEntryAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT page_index, slot, skill_id, level, extra_values FROM character_skills "
            + "WHERE character_id=@cid AND mode='pve' ORDER BY page_index, slot",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            Skill,
            cancellationToken);

    public async ValueTask InsertAsync(
        CharacterId characterId, int slot, int skillId, int level, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            """
            INSERT INTO character_skills (character_id, mode, page_index, slot, skill_id, level, extra_values)
            VALUES (@cid, 'pve', 0, @slot, @skill, @level, NULL);
            """,
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@slot", slot);
                parameters.AddWithValue("@skill", skillId);
                parameters.AddWithValue("@level", level);
            },
            cancellationToken).ConfigureAwait(false);

    public ValueTask<int> SetLevelAsync(
        CharacterId characterId, int pageIndex, int slot, int level, CancellationToken cancellationToken) =>
        database.ExecuteAsync(
            "UPDATE character_skills SET level=@level "
            + "WHERE character_id=@cid AND mode='pve' AND page_index=@page AND slot=@slot",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@page", pageIndex);
                parameters.AddWithValue("@slot", slot);
                parameters.AddWithValue("@level", level);
            },
            cancellationToken);

    public ValueTask<int> RemoveAsync(
        CharacterId characterId, int pageIndex, int slot, CancellationToken cancellationToken) =>
        database.ExecuteAsync(
            "DELETE FROM character_skills "
            + "WHERE character_id=@cid AND mode='pve' AND page_index=@page AND slot=@slot",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@page", pageIndex);
                parameters.AddWithValue("@slot", slot);
            },
            cancellationToken);

    public ValueTask<IReadOnlyList<CharacterSkillEntryRow>> ReadDuelSkillsAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT page_index, slot, skill_id, level, extra_values FROM character_skills
            WHERE character_id=@cid AND mode='pvp' ORDER BY page_index, slot
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            Skill,
            cancellationToken);

    public async ValueTask InsertDuelSkillAsync(
        CharacterId characterId,
        int pageIndex,
        int slot,
        int skillId,
        int level,
        byte[] extraValues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extraValues);
        _ = await database.ExecuteAsync(
            """
            INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level,extra_values)
            VALUES(@cid,'pvp',@page,@slot,@skill,@level,@extra)
            """,
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@page", pageIndex);
                parameters.AddWithValue("@slot", slot);
                parameters.AddWithValue("@skill", skillId);
                parameters.AddWithValue("@level", level);
                parameters.AddWithValue(
                    "@extra", extraValues.Length > 0 ? extraValues : DBNull.Value);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One row, however the caller means to use it.</summary>
    /// <remarks>
    /// Nothing stored is read: a row with no extra values reads as an empty array, which is what
    /// both reads answered before they were one.
    /// </remarks>
    private static CharacterSkillEntryRow Skill(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
            EntryBlob.Bounded(reader, 4, ExtraValuesBound, "skill extra values", allowNull: true));
}
