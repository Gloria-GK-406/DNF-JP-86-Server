using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Characters;

/// <summary>The <c>character_skills</c> table.</summary>
internal sealed class SqliteCharacterSkillTable(SqliteDatabase database) : ICharacterSkillTable
{
    public ValueTask<IReadOnlyList<CharacterSkillLevelRow>> ReadLevelsAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT page_index,slot,skill_id,level FROM character_skills WHERE character_id=@cid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterSkillLevelRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
            cancellationToken);

    public ValueTask<IReadOnlyList<CharacterSkillEntryRow>> ReadEntryAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT page_index, slot, skill_id, level, extra_values FROM character_skills "
            + "WHERE character_id=@cid ORDER BY page_index, slot",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterSkillEntryRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                EntryBlob.Bounded(reader, 4, 1024, "skill extra values", allowNull: true)),
            cancellationToken);

    public async ValueTask InsertAsync(
        CharacterId characterId, int slot, int skillId, int level, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            """
            INSERT INTO character_skills (character_id, page_index, slot, skill_id, level, extra_values)
            VALUES (@cid, 0, @slot, @skill, @level, NULL);
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
            + "WHERE character_id=@cid AND page_index=@page AND slot=@slot",
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
            "DELETE FROM character_skills WHERE character_id=@cid AND page_index=@page AND slot=@slot",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@page", pageIndex);
                parameters.AddWithValue("@slot", slot);
            },
            cancellationToken);
}

/// <summary>The <c>character_pvp_skills</c> table.</summary>
internal sealed class SqliteCharacterPvpSkillTable(SqliteDatabase database) : ICharacterPvpSkillTable
{
    public ValueTask<IReadOnlyList<CharacterPvpSkillRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT page_index, slot, skill_id, level, extra_values
            FROM character_pvp_skills WHERE character_id=@cid ORDER BY page_index, slot
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterPvpSkillRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.IsDBNull(4) ? [] : (byte[])reader.GetValue(4)),
            cancellationToken);

    public async ValueTask InsertAsync(
        CharacterId characterId,
        int pageIndex,
        int slot,
        int skillId,
        int level,
        byte[] extraValues,
        CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            """
            INSERT INTO character_pvp_skills(character_id,page_index,slot,skill_id,level,extra_values)
            VALUES(@cid,@page,@slot,@skill,@level,@extra)
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

