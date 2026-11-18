using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

/// <summary>The <c>character_skills</c> table.</summary>
internal sealed class SqliteCharacterSkillTable(SqliteDatabase database) : ICharacterSkillTable
{
    /// <summary>How big a skill's extra values may be before the row is refused.</summary>
    /// <remarks>
    /// It was the ordinary pages' bound and is now both, because it is one column. A duel row past
    /// it cannot be anything the seeding path wrote, for the same reason an ordinary one cannot.
    /// </remarks>
    private const int ExtraValuesBound = 1024;

    // Called under the command transaction. UPDATE preserves level and custom command bytes.
    public async ValueTask SwapSlotsAsync(CharacterId characterId, int pageIndex, int first, int second,
        CancellationToken ct, SkillGroup group = SkillGroup.Pve)
    {
        if (first == second) return;
        if (first is < 0 or > 203 || second is < 0 or > 203) throw new ArgumentOutOfRangeException(nameof(first));
        // A temporary out-of-wire slot avoids the immediate SQLite unique-key check.
        await Move(first, -1);
        await Move(second, first);
        await Move(-1, second);
        async ValueTask Move(int from, int to) => _ = await database.ExecuteAsync(
            "UPDATE character_skills SET slot=@to WHERE character_id=@cid AND mode=@mode AND page_index=@page AND slot=@from",
            p =>
            {
                p.AddWithValue("@cid", characterId.Value);
                p.AddWithValue("@mode", Mode(group));
                p.AddWithValue("@page", pageIndex);
                p.AddWithValue("@from", from);
                p.AddWithValue("@to", to);
            }, ct);
    }


    public ValueTask<IReadOnlyList<CharacterSkillLevelRow>> ReadLevelsAsync(
        CharacterId characterId, CancellationToken cancellationToken, SkillGroup group = SkillGroup.Pve) =>
        database.ReadAllAsync(
            "SELECT page_index,slot,skill_id,level FROM character_skills "
            + "WHERE character_id=@cid AND mode=@mode",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@mode", Mode(group));
            },
            reader => new CharacterSkillLevelRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
            cancellationToken);

    public ValueTask<IReadOnlyList<CharacterSkillEntryRow>> ReadEntryAsync(
        CharacterId characterId, CancellationToken cancellationToken, SkillGroup group = SkillGroup.Pve) =>
        database.ReadAllAsync(
            "SELECT page_index, slot, skill_id, level, extra_values FROM character_skills "
            + "WHERE character_id=@cid AND mode=@mode ORDER BY page_index, slot",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@mode", Mode(group));
            },
            Skill,
            cancellationToken);

    public async ValueTask InsertAsync(
        CharacterId characterId, int pageIndex, int slot, int skillId, int level,
        CancellationToken cancellationToken, SkillGroup group = SkillGroup.Pve, byte[]? extraValues = null) =>
        _ = await database.ExecuteAsync(
            """
            INSERT INTO character_skills (character_id, mode, page_index, slot, skill_id, level, extra_values)
            VALUES (@cid, @mode, @page, @slot, @skill, @level, @extra);
            """,
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@mode", Mode(group));
                parameters.AddWithValue("@page", pageIndex);
                parameters.AddWithValue("@slot", slot);
                parameters.AddWithValue("@skill", skillId);
                parameters.AddWithValue("@level", level);
                parameters.AddWithValue("@extra", extraValues is { Length: > 0 } ? extraValues : DBNull.Value);
            },
            cancellationToken).ConfigureAwait(false);

    public ValueTask<int> SetLevelAsync(
        CharacterId characterId, int pageIndex, int slot, int level, CancellationToken cancellationToken,
        SkillGroup group = SkillGroup.Pve) =>
        database.ExecuteAsync(
            "UPDATE character_skills SET level=@level "
            + "WHERE character_id=@cid AND mode=@mode AND page_index=@page AND slot=@slot",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@mode", Mode(group));
                parameters.AddWithValue("@page", pageIndex);
                parameters.AddWithValue("@slot", slot);
                parameters.AddWithValue("@level", level);
            },
            cancellationToken);

    public ValueTask<int> RemoveAsync(
        CharacterId characterId, int pageIndex, int slot, CancellationToken cancellationToken,
        SkillGroup group = SkillGroup.Pve) =>
        database.ExecuteAsync(
            "DELETE FROM character_skills "
            + "WHERE character_id=@cid AND mode=@mode AND page_index=@page AND slot=@slot",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@mode", Mode(group));
                parameters.AddWithValue("@page", pageIndex);
                parameters.AddWithValue("@slot", slot);
            },
            cancellationToken);

    private static string Mode(SkillGroup group) => group switch
    {
        SkillGroup.Pve => "pve",
        SkillGroup.Duel => "pvp",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    /// <summary>One row, however the caller means to use it.</summary>
    /// <remarks>
    /// Nothing stored is read: a row with no extra values reads as an empty array, which is what
    /// both reads answered before they were one.
    /// </remarks>
    private static CharacterSkillEntryRow Skill(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
            EntryBlob.Bounded(reader, 4, ExtraValuesBound, "skill extra values", allowNull: true));
}
