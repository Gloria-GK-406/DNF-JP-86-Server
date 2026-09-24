using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

// One class per table, reading and writing the character's own six. The composition back into the
// records the domain already had happens in the domain, not here.

/// <summary>The <c>character_condition</c> table.</summary>
internal sealed partial class SqliteCharacterConditionTable(SqliteDatabase database) : ICharacterConditionTable
{
    public ValueTask<CharacterConditionRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT action_byte, aura_flag, black_count, champion_break_key_id, champion_break_mode,
            champion_break_value, channel_display_mode, channel_type, chaos_point,
            chat_ban_end_time, costume_flag, creature_buffer, creature_field1, creature_field2,
            creature_field3, creature_field4, crystal_cube_grade, crystal_cube_type,
            dimension_flag1, dimension_flag2, dimension_flag3, dimension_flag4, disguise_kind,
            emotion_index, fatigue_display_update, fatigue_penalty, fatigue_update,
            hardcore_death_count, is_disguised, is_event_character, is_hardcore_dead,
            is_hardcore_mode, is_premium_pc_room, is_private_store, is_return_user,
            link_slot_enabled, link_type_a, link_type_b, mood_value, pc_room_id, pc_room_state,
            pet_display_flag, return_user_flag, server_group_id, stamina, title_display_flag,
            trailing_byte
            FROM character_condition WHERE character_id=@cid;
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterConditionRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                reader.GetInt64(8), reader.GetInt64(9), reader.GetInt32(10),
                EntryBlob.Bounded(reader, 11, 8, "creature buffer", true), reader.GetInt32(12),
                reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15),
                reader.GetInt32(16), reader.GetInt32(17), reader.GetInt32(18),
                reader.GetInt32(19), reader.GetInt32(20), reader.GetInt32(21),
                reader.GetInt32(22), reader.GetInt32(23), reader.GetInt32(24),
                reader.GetInt64(25), reader.GetInt32(26), reader.GetInt32(27),
                reader.GetInt32(28), reader.GetInt32(29), reader.GetInt32(30),
                reader.GetInt32(31), reader.GetInt32(32), reader.GetInt32(33),
                reader.GetInt32(34), reader.GetInt32(35), reader.GetInt32(36),
                reader.GetInt32(37), reader.GetInt32(38), reader.GetInt64(39),
                reader.GetInt32(40), reader.GetInt32(41), reader.GetInt32(42),
                reader.GetInt32(43), reader.GetInt32(44), reader.GetInt32(45),
                reader.GetInt32(46)),
            cancellationToken);
}

/// <summary>The <c>character_stats</c> table.</summary>
internal sealed partial class SqliteCharacterStatsTable(SqliteDatabase database) : ICharacterStatsTable
{
    public ValueTask<CharacterStatsRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT auction_gold_limit, current_hp_mp_percent, equip_list_trailing,
            equipped_creature_level, flag_byte, gold_carry_limit, progress1, progress2,
            quest_shop_count, server_timestamp, stat_attack_speed, stat_cast_speed,
            stat_dark_resistance, stat_fire_resistance, stat_hit_recovery, stat_hp_max,
            stat_hp_regen_speed, stat_inventory_limit, stat_jump_power, stat_light_resistance,
            stat_magical_attack, stat_magical_defense, stat_move_speed, stat_mp_max,
            stat_mp_regen_speed, stat_physical_attack, stat_physical_defense,
            stat_water_resistance, stat_weight
            FROM character_stats WHERE character_id=@cid;
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterStatsRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                reader.GetInt32(8), reader.GetInt64(9), reader.GetInt32(10),
                reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13),
                reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16),
                reader.GetInt32(17), reader.GetInt32(18), reader.GetInt32(19),
                reader.GetInt32(20), reader.GetInt32(21), reader.GetInt32(22),
                reader.GetInt32(23), reader.GetInt32(24), reader.GetInt32(25),
                reader.GetInt32(26), reader.GetInt32(27), reader.GetInt64(28)),
            cancellationToken);
}

/// <summary>The <c>character_pvp</c> table.</summary>
internal sealed partial class SqliteCharacterPvpTable(SqliteDatabase database) : ICharacterPvpTable
{
    public ValueTask<CharacterPvpRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT pvp_grade, pvp_lose_streak, pvp_rank_point, pvp_rating_grade, pvp_stat_a,
            pvp_win_streak, skills_initialized
            FROM character_pvp WHERE character_id=@cid;
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterPvpRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetInt32(3),
                reader.GetInt64(4), reader.GetInt32(5), reader.GetInt32(6)),
            cancellationToken);
}

/// <summary>The <c>character_settings</c> table.</summary>
internal sealed partial class SqliteCharacterSettingsTable(SqliteDatabase database) : ICharacterSettingsTable
{
    public ValueTask<CharacterSettingsRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT character_option_blob, user_state_bits
            FROM character_settings WHERE character_id=@cid;
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterSettingsRow(
                // Nothing and nothing-at-all are different here: a character with no option
                // block sends four zero bytes, and one with an empty block sends none. The
                // reader that answers with an empty array for a NULL would collapse the two.
                EntryBlob.Optional(reader, 0, EntryBlob.Option, "character option"),
                reader.GetInt64(1)),
            cancellationToken);
}

/// <summary>The <c>character_expert_job</c> table.</summary>
internal sealed partial class SqliteCharacterExpertJobTable(SqliteDatabase database) : ICharacterExpertJobTable
{
    public ValueTask<CharacterExpertJobRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT disjoint_machine_endurance, disjoint_machine_grade, enchanter_endurance,
            expert_job_exp, expert_job_type, giveup_count
            FROM character_expert_job WHERE character_id=@cid;
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterExpertJobRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt64(3),
                reader.GetInt32(4), reader.GetInt32(5)),
            cancellationToken);
}

/// <summary>The <c>character_condition</c> table, written.</summary>
internal sealed partial class SqliteCharacterConditionTable
{
    public async ValueTask InsertIfAbsentAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            "INSERT INTO character_condition (character_id, pc_room_state) VALUES (@cid, 0) "
            + "ON CONFLICT(character_id) DO NOTHING;",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            cancellationToken).ConfigureAwait(false);
}

/// <summary>The <c>character_condition</c> table, read for a whole roster at once.</summary>
internal sealed partial class SqliteCharacterConditionTable
{
    public ValueTask<IReadOnlyList<CharacterPcRoomRow>> ReadPcRoomStatesAsync(
        IReadOnlyList<int> characterIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(characterIds);
        return characterIds.Count == 0
            ? ValueTask.FromResult<IReadOnlyList<CharacterPcRoomRow>>([])
            : database.ReadAllAsync(
                "SELECT character_id,pc_room_state FROM character_condition "
                + $"WHERE character_id IN ({CharacterIdList.Placeholders(characterIds)})",
                parameters => CharacterIdList.Bind(parameters, characterIds),
                reader => new CharacterPcRoomRow(reader.GetInt32(0), reader.GetInt32(1)),
                cancellationToken);
    }
}

/// <summary>The <c>character_pvp</c> table, written.</summary>
internal sealed partial class SqliteCharacterPvpTable
{
    public async ValueTask InsertIfAbsentAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            "INSERT INTO character_pvp (character_id) VALUES (@cid) "
            + "ON CONFLICT(character_id) DO NOTHING;",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask MarkSkillsInitializedAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            "UPDATE character_pvp SET skills_initialized=1 WHERE character_id=@cid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            cancellationToken).ConfigureAwait(false);
}

/// <summary>The <c>character_settings</c> table, written.</summary>
internal sealed partial class SqliteCharacterSettingsTable
{
    public async ValueTask SaveOptionAsync(CharacterId characterId, byte[] body, CancellationToken ct)
    {
        if (body.Length != 516 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body) != 512)
            throw new ArgumentException("A21 character options require a 512-byte payload.", nameof(body));
        _ = await database.ExecuteAsync(
            "INSERT INTO character_settings(character_id,character_option_blob) VALUES(@id,@body) "
            + "ON CONFLICT(character_id) DO UPDATE SET character_option_blob=excluded.character_option_blob",
            p => { p.AddWithValue("@id",characterId.Value); p.AddWithValue("@body",body); }, ct);
    }

    public async ValueTask InsertIfAbsentAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            "INSERT INTO character_settings (character_id) VALUES (@cid) "
            + "ON CONFLICT(character_id) DO NOTHING;",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            cancellationToken).ConfigureAwait(false);
}

/// <summary>The <c>character_expert_job</c> table, written.</summary>
internal sealed partial class SqliteCharacterExpertJobTable
{
    public async ValueTask InsertIfAbsentAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            "INSERT INTO character_expert_job (character_id) VALUES (@cid) "
            + "ON CONFLICT(character_id) DO NOTHING;",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            cancellationToken).ConfigureAwait(false);
}

/// <summary>The <c>character_stats</c> table, written.</summary>
internal sealed partial class SqliteCharacterStatsTable
{
    public async ValueTask InsertIfAbsentAsync(
        CharacterId characterId,
        CharacterBaseStats stats,
        int currentHpMpPercent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stats);
        _ = await database.ExecuteAsync(
            """
            INSERT INTO character_stats (
                character_id, stat_hp_max, stat_mp_max, stat_physical_attack,
                stat_physical_defense, stat_magical_attack, stat_magical_defense,
                stat_fire_resistance, stat_water_resistance, stat_dark_resistance,
                stat_light_resistance, stat_inventory_limit, stat_hp_regen_speed,
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
}

/// <summary>The <c>character_daily_challenges</c> table.</summary>
internal sealed class SqliteCharacterDailyChallengeTable(SqliteDatabase database)
    : ICharacterDailyChallengeTable
{
    public ValueTask<IReadOnlyList<DailyChallengeRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT group_index, group_id, claimed_at FROM character_daily_challenges "
            + "WHERE character_id=@cid ORDER BY group_index",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new DailyChallengeRow(
                reader.GetInt32(0), reader.GetInt32(1), !reader.IsDBNull(2)),
            cancellationToken);
}
