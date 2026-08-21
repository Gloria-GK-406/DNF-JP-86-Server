using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Characters;

/// <summary>
/// How big a stored blob may be before it is refused.
/// </summary>
/// <remarks>
/// These are about the column rather than about what is in it: a value past the bound cannot be
/// anything a client sent, so it is a sign the row is wrong. The check stays beside the read,
/// which is where it has always been.
/// </remarks>
internal static class EntryBlob
{
    public const int Appearance = 16 * 1024;
    public const int Option = 64 * 1024;
    public const int Combo = 64 * 1024;
    public const int CreatureText = 4 * 1024;

    /// <summary>The blob, empty or refused when there is none.</summary>
    public static byte[] Bounded(
        SqliteDataReader reader, int ordinal, int maximum, string name, bool allowNull)
    {
        if (reader.IsDBNull(ordinal))
        {
            return allowNull ? [] : throw new InvalidDataException($"Character {name} is unavailable.");
        }

        var value = (byte[])reader.GetValue(ordinal);
        return value.Length > maximum
            ? throw new InvalidDataException($"Character {name} exceeds {maximum} bytes.")
            : value;
    }

    /// <summary>The blob, or nothing at all when there is none.</summary>
    public static byte[]? Optional(
        SqliteDataReader reader, int ordinal, int maximum, string name)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = (byte[])reader.GetValue(ordinal);
        return value.Length > maximum
            ? throw new InvalidDataException($"Character {name} exceeds {maximum} bytes.")
            : value;
    }
}

/// <summary>The entry-load reads of <c>characters</c>.</summary>
internal sealed class SqliteCharacterEntryTable(SqliteDatabase database) : ICharacterEntryTable
{
    public ValueTask<CharacterEntryRow?> FindEntryAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT account_id, job, level, clone_title_item_id, appearance_blob,
                   exp, ex_equip_slot_stat, pvp_grade, pvp_rating_grade, user_state
            FROM characters
            WHERE character_id=@cid AND delete_flag=0;
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterEntryRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt64(3),
                EntryBlob.Bounded(reader, 4, EntryBlob.Appearance, "appearance", allowNull: true),
                reader.GetInt64(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8),
                reader.GetInt32(9)),
            cancellationToken);

    public ValueTask<BonusPointRow?> FindBonusPointsAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            "SELECT bonus_sp, bonus_tp FROM characters WHERE character_id=@cid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new BonusPointRow(reader.GetInt32(0), reader.GetInt32(1)),
            cancellationToken);
}

/// <summary>The entry-load reads of <c>character_subtype0_fields</c>.</summary>
internal sealed class SqliteCharacterSubtype0EntryTable(SqliteDatabase database)
    : ICharacterSubtype0EntryTable
{
    public ValueTask<CharacterSubtype0StorageRecord?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT stamina, fatigue_penalty, is_event_character, is_private_store,
                   expert_job_type, expert_job_exp, is_hardcore_mode, is_hardcore_dead,
                   hardcore_death_count, user_state_bits, chat_ban_end_time, mood_value,
                   pvp_rank_point,
                   creature_field1, creature_field2, creature_field3, creature_field4,
                   creature_buffer, pc_room_id, is_premium_pc_room, server_group_id,
                   black_count, guild_level, chaos_point, disguise_kind, is_disguised,
                   fatigue_update, return_user_flag, channel_display_mode, channel_type,
                   is_return_user, link_slot_enabled, link_type_a, link_type_b,
                   emotion_index, action_byte, fatigue_display_update, costume_flag,
                   aura_flag, pet_display_flag, title_display_flag, pvp_stat_a,
                   pvp_win_streak, pvp_lose_streak, trailing_byte
            FROM character_subtype0_fields WHERE character_id=@cid;
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterSubtype0StorageRecord(
                reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt64(5), reader.GetInt32(6), reader.GetInt32(7),
                reader.GetInt32(8), reader.GetInt64(9), reader.GetInt64(10), reader.GetInt32(11),
                reader.GetInt64(12),
                reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16),
                EntryBlob.Bounded(reader, 17, 8, "creature buffer", true), reader.GetInt64(18),
                reader.GetInt32(19), reader.GetInt32(20), reader.GetInt64(21), reader.GetInt32(22),
                reader.GetInt64(23), reader.GetInt32(24), reader.GetInt32(25), reader.GetInt32(26),
                reader.GetInt32(27), reader.GetInt32(28), reader.GetInt32(29), reader.GetInt32(30),
                reader.GetInt32(31), reader.GetInt32(32), reader.GetInt32(33), reader.GetInt32(34),
                reader.GetInt32(35), reader.GetInt32(36), reader.GetInt32(37), reader.GetInt32(38),
                reader.GetInt32(39), reader.GetInt32(40), reader.GetInt64(41),
                reader.GetInt32(42), reader.GetInt32(43), reader.GetInt32(44)),
            cancellationToken);

    public async ValueTask<(int Type, int Experience)?> FindExpertJobAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        await database.ReadFirstAsync(
            "SELECT expert_job_type, expert_job_exp FROM character_subtype0_fields "
            + "WHERE character_id=@cid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => Tuple.Create(reader.GetInt32(0), reader.GetInt32(1)),
            cancellationToken).ConfigureAwait(false) is { } pair
            ? (pair.Item1, pair.Item2)
            : null;
}

/// <summary>The entry-load reads of <c>character_subtype1_fields</c>.</summary>
internal sealed class SqliteCharacterSubtype1EntryTable(SqliteDatabase database)
    : ICharacterSubtype1EntryTable
{
    public ValueTask<CharacterSubtype1StorageRecord?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT stat_hp_max, stat_mp_max, stat_physical_attack, stat_physical_defense,
                   stat_magical_attack, stat_magical_defense, stat_move_speed, stat_attack_speed,
                   stat_cast_speed, stat_inventory_limit, skill_tree_index, progress1, progress2,
                   stat_fire_resistance, stat_water_resistance, stat_dark_resistance,
                   stat_light_resistance, stat_hp_regen_speed, stat_mp_regen_speed,
                   stat_hit_recovery, stat_jump_power, stat_weight, current_hp_mp_percent,
                   name_tag_item_id, name_tag_expire_time, equipped_creature_level,
                   equip_list_trailing, flag_byte, guild_power_war, server_timestamp,
                   quest_shop_count, manage_level
            FROM character_subtype1_fields WHERE character_id=@cid;
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterSubtype1StorageRecord(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11),
                reader.GetInt32(12),
                reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16),
                reader.GetInt32(17), reader.GetInt32(18), reader.GetInt32(19), reader.GetInt32(20),
                reader.GetInt64(21), reader.GetInt32(22), reader.GetInt64(23), reader.GetInt64(24),
                reader.GetInt32(25), reader.GetInt32(26), reader.GetInt32(27), reader.GetInt64(28),
                reader.GetInt64(29), reader.GetInt32(30), reader.GetInt32(31),
                // The normalized character_pvp_results table was retired by the replay-removal
                // migration after proving it had no writer and was empty. Until a new
                // authoritative PvP persistence source exists, row 9's PvP collection is
                // explicitly loaded-empty.
                PvpResults: []),
            cancellationToken);
}

/// <summary>The entry-load reads of <c>character_init_flags</c>.</summary>
internal sealed class SqliteCharacterInitFlagEntryTable(SqliteDatabase database)
    : ICharacterInitFlagEntryTable
{
    public async ValueTask<(byte[]? Option, bool Present)> FindOptionAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var found = await database.ReadAllAsync(
            "SELECT character_option_blob FROM character_init_flags WHERE character_id=@cid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => EntryBlob.Optional(reader, 0, EntryBlob.Option, "character option"),
            cancellationToken).ConfigureAwait(false);
        return found.Count == 0 ? (null, false) : (found[0], true);
    }

    public ValueTask<ChampionBreakRow?> FindChampionBreakAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            "SELECT champion_break_key_id, champion_break_mode, champion_break_value "
            + "FROM character_init_flags WHERE character_id=@cid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new ChampionBreakRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
            cancellationToken);
}

/// <summary>The <c>character_hotkey_slots</c> table.</summary>
internal sealed class SqliteCharacterHotkeyTable(SqliteDatabase database) : ICharacterHotkeyTable
{
    public ValueTask<IReadOnlyList<int>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT hotkey_value FROM character_hotkey_slots WHERE character_id=@cid "
            + "ORDER BY slot_index",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => reader.GetInt32(0),
            cancellationToken);
}

/// <summary>The <c>character_dimensions</c> table.</summary>
internal sealed class SqliteCharacterDimensionTable(SqliteDatabase database)
    : ICharacterDimensionTable
{
    public ValueTask<IReadOnlyList<DimensionRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT sort_order, dim_key, val1, val2 FROM character_dimensions "
            + "WHERE character_id=@cid ORDER BY sort_order",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new DimensionRow(
                reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3)),
            cancellationToken);
}

/// <summary>The <c>character_dimension_flags</c> table.</summary>
internal sealed class SqliteCharacterDimensionFlagTable(SqliteDatabase database)
    : ICharacterDimensionFlagTable
{
    public async ValueTask<(int, int, int, int)> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var rows = await database.ReadAllAsync(
            "SELECT flag1, flag2, flag3, flag4 FROM character_dimension_flags "
            + "WHERE character_id=@cid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => Tuple.Create(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
            cancellationToken).ConfigureAwait(false);
        return rows.Count == 0
            ? (0, 0, 0, 0)
            : (rows[0].Item1, rows[0].Item2, rows[0].Item3, rows[0].Item4);
    }
}

/// <summary>The <c>character_dark_knight_combo_skill_pages</c> table.</summary>
internal sealed class SqliteCharacterComboPageTable(SqliteDatabase database)
    : ICharacterComboPageTable
{
    public ValueTask<IReadOnlyList<ComboPageRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT page_index, body FROM character_dark_knight_combo_skill_pages "
            + "WHERE character_id=@cid ORDER BY page_index",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new ComboPageRow(
                reader.GetInt32(0),
                EntryBlob.Bounded(reader, 1, EntryBlob.Combo, "Dark Knight combo", allowNull: false)),
            cancellationToken);
}

/// <summary>The <c>character_knight_shield_deck</c> table.</summary>
internal sealed class SqliteCharacterShieldDeckTable(SqliteDatabase database)
    : ICharacterShieldDeckTable
{
    public ValueTask<IReadOnlyList<ShieldSlotRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT slot_index, shield_item_id FROM character_knight_shield_deck "
            + "WHERE character_id=@cid ORDER BY slot_index",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new ShieldSlotRow(reader.GetInt32(0), reader.GetInt32(1)),
            cancellationToken);
}

/// <summary>The <c>character_active_quests</c> table.</summary>
internal sealed class SqliteCharacterActiveQuestTable(SqliteDatabase database)
    : ICharacterActiveQuestTable
{
    public ValueTask<IReadOnlyList<ActiveQuestRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT slot, quest_id, trigger_value FROM character_active_quests "
            + "WHERE character_id=@cid ORDER BY slot",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new ActiveQuestRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
            cancellationToken);
}

/// <summary>The <c>character_quest_notify_selections</c> table.</summary>
internal sealed class SqliteCharacterQuestNotifyTable(SqliteDatabase database)
    : ICharacterQuestNotifyTable
{
    public ValueTask<IReadOnlyList<int>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT quest_id FROM character_quest_notify_selections WHERE character_id=@cid "
            + "ORDER BY slot_index",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => reader.GetInt32(0),
            cancellationToken);
}

/// <summary>The <c>character_invisible_falgs</c> table.</summary>
/// <remarks>The name is the original server's spelling, kept so the column can be found.</remarks>
internal sealed class SqliteCharacterQuestClearFlagTable(SqliteDatabase database)
    : ICharacterQuestClearFlagTable
{
    public ValueTask<IReadOnlyList<QuestClearFlagRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT slot_index, flag_value FROM character_invisible_falgs "
            + "WHERE character_id=@cid ORDER BY slot_index",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new QuestClearFlagRow(reader.GetInt32(0), reader.GetInt32(1)),
            cancellationToken);
}

/// <summary>The <c>character_growth_weapon_stages</c> table.</summary>
internal sealed class SqliteCharacterGrowthWeaponTable(SqliteDatabase database)
    : ICharacterGrowthWeaponTable
{
    public ValueTask<IReadOnlyList<int>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT stage_id FROM character_growth_weapon_stages WHERE character_id=@cid "
            + "ORDER BY sort_order",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => reader.GetInt32(0),
            cancellationToken);
}

/// <summary>The <c>character_crystal_contract</c> table.</summary>
internal sealed class SqliteCharacterCrystalContractTable(SqliteDatabase database)
    : ICharacterCrystalContractTable
{
    public ValueTask<CrystalContractRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            "SELECT cube_type, cube_grade FROM character_crystal_contract WHERE character_id=@cid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CrystalContractRow(reader.GetInt32(0), reader.GetInt32(1)),
            cancellationToken);
}

/// <summary>The <c>character_daily_challenge_groups</c> table.</summary>
internal sealed class SqliteCharacterDailyChallengeGroupTable(SqliteDatabase database)
    : ICharacterDailyChallengeGroupTable
{
    public ValueTask<IReadOnlyList<DailyChallengeGroupRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT group_index, group_id FROM character_daily_challenge_groups "
            + "WHERE character_id=@cid ORDER BY group_index",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new DailyChallengeGroupRow(reader.GetInt32(0), reader.GetInt32(1)),
            cancellationToken);
}

/// <summary>The <c>character_daily_challenge_claims</c> table.</summary>
internal sealed class SqliteCharacterDailyChallengeClaimTable(SqliteDatabase database)
    : ICharacterDailyChallengeClaimTable
{
    public ValueTask<IReadOnlyList<int>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT group_index FROM character_daily_challenge_claims WHERE character_id=@cid "
            + "ORDER BY group_index",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => reader.GetInt32(0),
            cancellationToken);
}

/// <summary>The <c>character_daily_challenge_entries</c> table.</summary>
internal sealed class SqliteCharacterDailyChallengeEntryTable(SqliteDatabase database)
    : ICharacterDailyChallengeEntryTable
{
    public ValueTask<IReadOnlyList<DailyChallengeEntryRow>> ReadAsync(
        CharacterId characterId, int groupIndex, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT entry_index, track_like_id, value_a, value_b "
            + "FROM character_daily_challenge_entries "
            + "WHERE character_id=@cid AND group_index=@group ORDER BY entry_index",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@group", groupIndex);
            },
            reader => new DailyChallengeEntryRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
            cancellationToken);
}

/// <summary>The <c>character_daily_challenge_tail_ids</c> table.</summary>
internal sealed class SqliteCharacterDailyChallengeTailTable(SqliteDatabase database)
    : ICharacterDailyChallengeTailTable
{
    public ValueTask<IReadOnlyList<int>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT id_value FROM character_daily_challenge_tail_ids WHERE character_id=@cid "
            + "ORDER BY sort_order",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => reader.GetInt32(0),
            cancellationToken);
}

/// <summary>The <c>character_daily_schedule_states</c> table.</summary>
internal sealed class SqliteCharacterDailyScheduleTable(SqliteDatabase database)
    : ICharacterDailyScheduleTable
{
    public ValueTask<IReadOnlyList<DailyScheduleRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT sort_order, param_a, mode_or_state, content_id, param_b "
            + "FROM character_daily_schedule_states WHERE character_id=@cid ORDER BY sort_order",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new DailyScheduleRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4)),
            cancellationToken);
}

/// <summary>The <c>character_dungeon_permissions</c> table.</summary>
internal sealed class SqliteCharacterDungeonPermissionTable(SqliteDatabase database)
    : ICharacterDungeonPermissionTable
{
    public ValueTask<IReadOnlyList<DungeonPermissionRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT sort_order, dungeon_id, clear_state FROM character_dungeon_permissions "
            + "WHERE character_id=@cid ORDER BY sort_order",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new DungeonPermissionRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
            cancellationToken);
}

/// <summary>The <c>character_creatures</c> table.</summary>
internal sealed class SqliteCharacterCreatureTable(SqliteDatabase database)
    : ICharacterCreatureTable
{
    public ValueTask<IReadOnlyList<CharacterPetStorageRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT sort_order, creature_key, field04, mode_flag, progress_value, mode1_field0a, "
            + "mode1_field0b, field_after_value, creature_text, tail_flag FROM character_creatures "
            + "WHERE character_id=@cid ORDER BY sort_order",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterPetStorageRow(
                reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                EntryBlob.Bounded(reader, 8, EntryBlob.CreatureText, "creature text", true),
                reader.GetInt32(9)),
            cancellationToken);
}

/// <summary>The <c>character_new_titlebook</c> table.</summary>
internal sealed class SqliteCharacterTitleBookTable(SqliteDatabase database)
    : ICharacterTitleBookTable
{
    public ValueTask<IReadOnlyList<TitleBookRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT category, slot_index, item_core FROM character_new_titlebook "
            + "WHERE character_id=@cid ORDER BY category, slot_index",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new TitleBookRow(
                // Handed up as read. The category narrows to a byte only after it has been found
                // to fit, so an out-of-range one reaches the refusal that names it rather than
                // failing as a conversion on the way there.
                reader.GetInt32(0),
                reader.GetInt32(1),
                EntryBlob.Bounded(reader, 2, 82, "title book item core", false)),
            cancellationToken);
}

/// <summary>The <c>character_achievement_complete</c> table.</summary>
internal sealed class SqliteCharacterAchievementTable(SqliteDatabase database)
    : ICharacterAchievementTable
{
    public ValueTask<IReadOnlyList<CharacterAchievementStorageRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT sort_order, achievement_id, p1, p2, p3, p4 "
            + "FROM character_achievement_complete WHERE character_id=@cid ORDER BY sort_order",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterAchievementStorageRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5)),
            cancellationToken);
}

/// <summary>The <c>character_collectbox_slots</c> table.</summary>
internal sealed class SqliteCharacterCollectionBoxTable(SqliteDatabase database)
    : ICharacterCollectionBoxTable
{
    public ValueTask<IReadOnlyList<CollectionBoxRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT box_index, slot_index, item_id FROM character_collectbox_slots "
            + "WHERE character_id=@cid ORDER BY box_index, slot_index",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CollectionBoxRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
            cancellationToken);
}

/// <summary>The <c>character_expert_job</c> table.</summary>
internal sealed class SqliteCharacterExpertJobTable(SqliteDatabase database)
    : ICharacterExpertJobTable
{
    public ValueTask<ExpertJobRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            "SELECT giveup_count, disjoint_machine_grade, disjoint_machine_endurance, "
            + "enchanter_endurance FROM character_expert_job WHERE character_id=@cid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new ExpertJobRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
            cancellationToken);
}

/// <summary>The <c>character_expert_job_recipes</c> table.</summary>
internal sealed class SqliteCharacterExpertRecipeTable(SqliteDatabase database)
    : ICharacterExpertRecipeTable
{
    public ValueTask<IReadOnlyList<int>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT recipe_id FROM character_expert_job_recipes WHERE character_id=@cid "
            + "ORDER BY recipe_id",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => reader.GetInt32(0),
            cancellationToken);
}

/// <summary>The <c>character_pvp_missions</c> table.</summary>
internal sealed class SqliteCharacterPvpMissionTable(SqliteDatabase database)
    : ICharacterPvpMissionTable
{
    public ValueTask<IReadOnlyList<PvpMissionRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT sort_order, mission_id, progress_value FROM character_pvp_missions "
            + "WHERE character_id=@cid ORDER BY sort_order",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new PvpMissionRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
            cancellationToken);
}

/// <summary>What a <c>character_daily_counters</c> row is at.</summary>
internal sealed class SqliteCharacterDailyCounterValueTable(SqliteDatabase database)
    : ICharacterDailyCounterValueTable
{
    public async ValueTask<int?> FindValueAsync(
        CharacterId characterId, string counterKey, CancellationToken cancellationToken)
    {
        var rows = await database.ReadAllAsync(
            "SELECT value FROM character_daily_counters "
            + "WHERE character_id=@cid AND counter_key=@key",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@key", counterKey);
            },
            reader => reader.IsDBNull(0) ? (int?)null : reader.GetInt32(0),
            cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }
}
