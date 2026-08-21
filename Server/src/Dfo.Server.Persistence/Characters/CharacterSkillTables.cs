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

/// <summary>The <c>character_pvp_skill_state</c> table.</summary>
internal sealed class SqliteCharacterPvpSkillStateTable(SqliteDatabase database)
    : ICharacterPvpSkillStateTable
{
    public ValueTask<bool> ExistsAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        Present(database.ReadFirstAsync(
            "SELECT 1 FROM character_pvp_skill_state WHERE character_id=@cid LIMIT 1",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            _ => Marker,
            cancellationToken));

    public async ValueTask InsertAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            "INSERT INTO character_pvp_skill_state(character_id) VALUES(@cid)",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            cancellationToken).ConfigureAwait(false);

    /// <summary>Stands in for the row, whose presence is all it says.</summary>
    private static readonly object Marker = new();

    private static async ValueTask<bool> Present(ValueTask<object?> row) =>
        await row.ConfigureAwait(false) is not null;
}

/// <summary>The <c>character_subtype0_fields</c> table.</summary>
internal sealed class SqliteCharacterSubtype0Table(SqliteDatabase database) : ICharacterSubtype0Table
{
    public async ValueTask InsertIfAbsentAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            "INSERT INTO character_subtype0_fields (character_id) VALUES (@cid) "
            + "ON CONFLICT(character_id) DO NOTHING;",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            cancellationToken).ConfigureAwait(false);
}

/// <summary>The <c>character_subtype1_fields</c> table.</summary>
internal sealed class SqliteCharacterSubtype1Table(SqliteDatabase database) : ICharacterSubtype1Table
{
    public async ValueTask InsertIfAbsentAsync(
        CharacterId characterId,
        CharacterBaseStats stats,
        int currentHpMpPercent,
        CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            """
            INSERT INTO character_subtype1_fields (
                character_id, stat_hp_max, stat_mp_max, stat_physical_attack, stat_physical_defense,
                stat_magical_attack, stat_magical_defense, stat_fire_resistance, stat_water_resistance,
                stat_dark_resistance, stat_light_resistance, stat_inventory_limit, stat_hp_regen_speed,
                stat_mp_regen_speed, stat_move_speed, stat_attack_speed, stat_cast_speed,
                stat_hit_recovery, stat_jump_power, stat_weight, current_hp_mp_percent)
            VALUES (
                @cid, @hpMax, @mpMax, @physicalAttack, @physicalDefense, @magicalAttack,
                @magicalDefense, @fireResistance, @waterResistance, @darkResistance,
                @lightResistance, @inventoryLimit, @hpRegenSpeed, @mpRegenSpeed, @moveSpeed,
                @attackSpeed, @castSpeed, @hitRecovery, @jumpPower, @weight, @currentHpMpPercent)
            ON CONFLICT(character_id) DO NOTHING;
            """,
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@hpMax", stats.HpMax);
                parameters.AddWithValue("@mpMax", stats.MpMax);
                parameters.AddWithValue("@physicalAttack", stats.PhysicalAttack);
                parameters.AddWithValue("@physicalDefense", stats.PhysicalDefense);
                parameters.AddWithValue("@magicalAttack", stats.MagicalAttack);
                parameters.AddWithValue("@magicalDefense", stats.MagicalDefense);
                parameters.AddWithValue("@fireResistance", stats.FireResistance);
                parameters.AddWithValue("@waterResistance", stats.WaterResistance);
                parameters.AddWithValue("@darkResistance", stats.DarkResistance);
                parameters.AddWithValue("@lightResistance", stats.LightResistance);
                parameters.AddWithValue("@inventoryLimit", stats.InventoryLimit);
                parameters.AddWithValue("@hpRegenSpeed", stats.HpRegenSpeed);
                parameters.AddWithValue("@mpRegenSpeed", stats.MpRegenSpeed);
                parameters.AddWithValue("@moveSpeed", stats.MoveSpeed);
                parameters.AddWithValue("@attackSpeed", stats.AttackSpeed);
                parameters.AddWithValue("@castSpeed", stats.CastSpeed);
                parameters.AddWithValue("@hitRecovery", stats.HitRecovery);
                parameters.AddWithValue("@jumpPower", stats.JumpPower);
                parameters.AddWithValue("@weight", stats.Weight);
                parameters.AddWithValue("@currentHpMpPercent", currentHpMpPercent);
            },
            cancellationToken).ConfigureAwait(false);
}
