using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Characters;

internal sealed class SqliteCharacterEntryStateRepository : ICharacterEntryStateRepository
{
    private const int AppearanceMaximumBytes = 16 * 1024;
    private const int OptionMaximumBytes = 64 * 1024;
    private const int ComboMaximumBytes = 64 * 1024;
    private const int CreatureTextMaximumBytes = 4 * 1024;
    private readonly string connectionString;

    public SqliteCharacterEntryStateRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        EnsureSchema();
    }

    public async ValueTask<CharacterEntryStorageRecord> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        if (accountId.Value <= 0 || characterId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Identifiers must be positive.");
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var profile = await LoadProfileAsync(connection, transaction, accountId, characterId, cancellationToken)
            .ConfigureAwait(false);
        var skills = await LoadSkillsAsync(connection, transaction, characterId, profile.Job, cancellationToken)
            .ConfigureAwait(false);
        var quests = await LoadQuestsAsync(connection, transaction, characterId, cancellationToken)
            .ConfigureAwait(false);
        var progression = await LoadProgressionAsync(
            connection, transaction, characterId, profile.Level, cancellationToken).ConfigureAwait(false);
        var features = await LoadFeaturesAsync(connection, transaction, characterId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new CharacterEntryStorageRecord(
            accountId, characterId, profile, skills, quests, progression, features);
    }

    private static async ValueTask<CharacterProfileStorageRecord> LoadProfileAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT c.account_id, c.job, c.level, c.clone_title_item_id, c.appearance_blob,
                   s0.stamina, s0.fatigue_penalty, s0.is_event_character,
                   s0.is_private_store, s0.expert_job_type, s0.expert_job_exp,
                   s0.is_hardcore_mode, s0.is_hardcore_dead, s0.hardcore_death_count,
                   s0.user_state_bits, s0.chat_ban_end_time, s0.mood_value, s0.pvp_rank_point,
                   s1.stat_hp_max, s1.stat_mp_max, s1.stat_physical_attack,
                   s1.stat_physical_defense, s1.stat_magical_attack, s1.stat_magical_defense,
                   s1.stat_move_speed, s1.stat_attack_speed, s1.stat_cast_speed,
                   s1.stat_inventory_limit, s1.skill_tree_index, s1.progress1, s1.progress2,
                   f.character_option_blob,
                   s0.creature_field1, s0.creature_field2, s0.creature_field3, s0.creature_field4,
                   s0.creature_buffer, s0.pc_room_id, s0.is_premium_pc_room, s0.server_group_id,
                   s0.black_count, s0.guild_level, s0.chaos_point, s0.disguise_kind, s0.is_disguised,
                   s0.fatigue_update, s0.return_user_flag, s0.channel_display_mode, s0.channel_type,
                   s0.is_return_user, s0.link_slot_enabled, s0.link_type_a, s0.link_type_b,
                   s0.emotion_index, s0.action_byte, s0.fatigue_display_update, s0.costume_flag,
                   s0.aura_flag, s0.pet_display_flag, s0.title_display_flag, s0.pvp_stat_a,
                   s0.pvp_win_streak, s0.pvp_lose_streak, s0.trailing_byte
                   ,s1.stat_fire_resistance, s1.stat_water_resistance, s1.stat_dark_resistance,
                   s1.stat_light_resistance, s1.stat_hp_regen_speed, s1.stat_mp_regen_speed,
                   s1.stat_hit_recovery, s1.stat_jump_power, s1.stat_weight, s1.stat_level,
                   s1.name_tag_item_id, s1.name_tag_expire_time, s1.equipped_creature_level,
                   s1.equip_list_trailing, s1.flag_byte, s1.guild_power_war,
                   s1.server_timestamp, s1.quest_shop_count, c.exp, c.ex_equip_slot_stat,
                   c.pvp_grade, c.pvp_rating_grade, c.user_state, s1.manage_level
            FROM characters c
            JOIN character_subtype0_fields s0 ON s0.character_id=c.character_id
            JOIN character_subtype1_fields s1 ON s1.character_id=c.character_id
            JOIN character_init_flags f ON f.character_id=c.character_id
            WHERE c.character_id=@cid AND c.delete_flag=0;
            """, characterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException($"Character {characterId.Value} has incomplete required profile state.");
        }

        if (reader.GetInt32(0) != accountId.Value)
        {
            throw new InvalidDataException(
                $"Character {characterId.Value} does not belong to account {accountId.Value}.");
        }

        var appearance = ReadBoundedBlob(reader, 4, AppearanceMaximumBytes, "appearance", allowNull: true);
        var option = ReadOptionalBoundedBlob(reader, 31, OptionMaximumBytes, "character option");
        var hotkeys = await ReadIntListAsync(connection, transaction,
            "SELECT hotkey_value FROM character_hotkey_slots WHERE character_id=@cid ORDER BY slot_index",
            characterId, cancellationToken).ConfigureAwait(false);
        if (hotkeys.Any(value => value is < 0 or > ushort.MaxValue) || hotkeys.Count > 256)
        {
            throw new InvalidDataException("Character hotkey state is out of range.");
        }

        var s0 = new CharacterSubtype0StorageRecord(
            reader.GetInt32(5), reader.GetInt64(6), reader.GetInt32(7), reader.GetInt32(8),
            reader.GetInt32(9), reader.GetInt64(10), reader.GetInt32(11), reader.GetInt32(12),
            reader.GetInt32(13), reader.GetInt64(14), reader.GetInt64(15), reader.GetInt32(16),
            reader.GetInt64(17),
            reader.GetInt32(32), reader.GetInt32(33), reader.GetInt32(34), reader.GetInt32(35),
            ReadBoundedBlob(reader, 36, 8, "creature buffer", true), reader.GetInt64(37),
            reader.GetInt32(38), reader.GetInt32(39), reader.GetInt64(40), reader.GetInt32(41),
            reader.GetInt64(42), reader.GetInt32(43), reader.GetInt32(44), reader.GetInt32(45),
            reader.GetInt32(46), reader.GetInt32(47), reader.GetInt32(48), reader.GetInt32(49),
            reader.GetInt32(50), reader.GetInt32(51), reader.GetInt32(52), reader.GetInt32(53),
            reader.GetInt32(54), reader.GetInt32(55), reader.GetInt32(56), reader.GetInt32(57),
            reader.GetInt32(58), reader.GetInt32(59), reader.GetInt64(60),
            reader.GetInt32(61), reader.GetInt32(62), reader.GetInt32(63));
        var s1 = new CharacterSubtype1StorageRecord(
            reader.GetInt32(18), reader.GetInt32(19), reader.GetInt32(20), reader.GetInt32(21),
            reader.GetInt32(22), reader.GetInt32(23), reader.GetInt32(24), reader.GetInt32(25),
            reader.GetInt32(26), reader.GetInt32(27), reader.GetInt32(28), reader.GetInt32(29),
            reader.GetInt32(30),
            reader.GetInt32(64), reader.GetInt32(65), reader.GetInt32(66), reader.GetInt32(67),
            reader.GetInt32(68), reader.GetInt32(69), reader.GetInt32(70), reader.GetInt32(71),
            reader.GetInt64(72), reader.GetInt32(73), reader.GetInt64(74), reader.GetInt64(75),
            reader.GetInt32(76), reader.GetInt32(77), reader.GetInt32(78), reader.GetInt64(79),
            reader.GetInt64(80), reader.GetInt32(81), reader.GetInt32(87),
            PvpResults: LoadRetiredPvpResults());
        ValidateProfile(s0, s1);
        var dimensions = new List<(int, long, int, int)>();
        await using (var dimensionCommand = CreateCommand(connection, transaction,
            "SELECT sort_order, dim_key, val1, val2 FROM character_dimensions WHERE character_id=@cid ORDER BY sort_order", characterId))
        await using (var dimensionReader = await dimensionCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await dimensionReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var value = (dimensionReader.GetInt32(0), dimensionReader.GetInt64(1),
                    dimensionReader.GetInt32(2), dimensionReader.GetInt32(3));
                if (value.Item1 < 0 || value.Item2 is < 0 or > uint.MaxValue
                    || value.Item3 is < 0 or > byte.MaxValue || value.Item4 is < 0 or > byte.MaxValue)
                    throw new InvalidDataException("Character dimension state is out of range.");
                dimensions.Add(value);
            }
        }
        var dimensionFlags = await ReadIntsOrDefaultAsync(connection, transaction,
            "SELECT flag1, flag2, flag3, flag4 FROM character_dimension_flags WHERE character_id=@cid",
            characterId, 4, cancellationToken).ConfigureAwait(false);
        if (dimensionFlags.Any(value => value is < 0 or > byte.MaxValue))
            throw new InvalidDataException("Character dimension flags are out of range.");
        return new CharacterProfileStorageRecord(
            checked((byte)reader.GetInt32(1)), checked((byte)reader.GetInt32(2)),
            checked((uint)reader.GetInt64(3)), appearance, s0, s1,
            hotkeys.Select(value => checked((ushort)value)).ToArray(), option,
            reader.GetInt64(82), reader.GetInt32(83), dimensions,
            (dimensionFlags[0], dimensionFlags[1], dimensionFlags[2], dimensionFlags[3]),
            checked((byte)reader.GetInt32(84)), checked((byte)reader.GetInt32(85)),
            checked((byte)reader.GetInt32(86)));
    }

    // The normalized character_pvp_results table was retired by the replay-removal
    // migration after proving it had no writer and was empty. Until a new authoritative
    // PvP persistence source exists, row 9's PvP collection is explicitly loaded-empty.
    private static IReadOnlyList<CharacterPvpResultStorageRow> LoadRetiredPvpResults() => [];

    private static async ValueTask<CharacterSkillStorageRecord> LoadSkillsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CharacterId characterId,
        byte job,
        CancellationToken cancellationToken)
    {
        var rows = new List<CharacterSkillStorageRow>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT page_index, slot, skill_id, level, extra_values FROM character_skills WHERE character_id=@cid ORDER BY page_index, slot", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var page = reader.GetInt32(0);
                var slot = reader.GetInt32(1);
                var skillId = reader.GetInt32(2);
                var level = reader.GetInt32(3);
                var extras = ReadBoundedBlob(reader, 4, 1024, "skill extra values", allowNull: true);
                if (page is < 0 or > 1 || slot is < 0 or > short.MaxValue
                    || skillId <= 0 || level is < 0 or > byte.MaxValue)
                {
                    throw new InvalidDataException("Character skill state is out of range.");
                }

                rows.Add(new CharacterSkillStorageRow(
                    checked((byte)page), checked((short)slot), skillId, level, extras));
            }
        }

        var combos = new List<(byte, byte[])>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT page_index, body FROM character_dark_knight_combo_skill_pages WHERE character_id=@cid ORDER BY page_index", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var page = reader.GetInt32(0);
                if (page is < 0 or > 1)
                {
                    throw new InvalidDataException("Dark Knight combo page is out of range.");
                }

                combos.Add((checked((byte)page), ReadBoundedBlob(
                    reader, 1, ComboMaximumBytes, "Dark Knight combo", allowNull: false)));
            }
        }

        var shields = new List<(byte, int)>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT slot_index, shield_item_id FROM character_knight_shield_deck WHERE character_id=@cid ORDER BY slot_index", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var slot = reader.GetInt32(0);
                var itemId = reader.GetInt32(1);
                if (slot is < 0 or > 4 || itemId <= 0)
                {
                    throw new InvalidDataException("Knight Shield state is out of range.");
                }
                shields.Add((checked((byte)slot), itemId));
            }
        }

        if (job != 9 && combos.Count != 0)
        {
            throw new InvalidDataException("Dark Knight combo state is inapplicable.");
        }
        if (job != 12 && shields.Count != 0)
        {
            throw new InvalidDataException("Knight Shield state is inapplicable.");
        }
        var bonuses = await ReadPairOrDefaultAsync(connection, transaction,
            "SELECT bonus_sp, bonus_tp FROM characters WHERE character_id=@cid",
            characterId, 0, 0, cancellationToken).ConfigureAwait(false);
        if (bonuses.Item1 < 0 || bonuses.Item2 < 0)
            throw new InvalidDataException("Character bonus skill points cannot be negative.");
        return new CharacterSkillStorageRecord(job, bonuses.Item1, bonuses.Item2, rows, combos, shields);
    }

    private static async ValueTask<CharacterQuestStorageRecord> LoadQuestsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var active = new List<CharacterActiveQuestStorageRow>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT slot, quest_id, trigger_value FROM character_active_quests WHERE character_id=@cid ORDER BY slot", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var slot = reader.GetInt32(0);
                var questId = reader.GetInt32(1);
                var trigger = reader.GetInt32(2);
                if (slot is < 0 or > short.MaxValue || questId <= 0 || trigger < 0)
                {
                    throw new InvalidDataException("Character active quest state is out of range.");
                }
                active.Add(new CharacterActiveQuestStorageRow(checked((short)slot), questId, trigger));
            }
        }
        var notifications = await ReadIntListAsync(connection, transaction,
            "SELECT quest_id FROM character_quest_notify_selections WHERE character_id=@cid ORDER BY slot_index",
            characterId, cancellationToken).ConfigureAwait(false);
        var flags = new List<(int, byte)>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT slot_index, flag_value FROM character_invisible_falgs WHERE character_id=@cid ORDER BY slot_index", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var slot = reader.GetInt32(0);
                var value = reader.GetInt32(1);
                if (slot is < 0 or >= 30_000 || value is < 0 or > byte.MaxValue)
                {
                    throw new InvalidDataException("Quest clear flag state is out of range.");
                }
                flags.Add((slot, checked((byte)value)));
            }
        }
        return new CharacterQuestStorageRecord(active, notifications, flags);
    }

    private static async ValueTask<CharacterProgressionStorageRecord> LoadProgressionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CharacterId characterId,
        byte level,
        CancellationToken cancellationToken)
    {
        var growth = await ReadIntListAsync(connection, transaction,
            "SELECT stage_id FROM character_growth_weapon_stages WHERE character_id=@cid ORDER BY sort_order",
            characterId, cancellationToken).ConfigureAwait(false);
        var (cubeType, cubeGrade) = await ReadPairOrDefaultAsync(connection, transaction,
            "SELECT cube_type, cube_grade FROM character_crystal_contract WHERE character_id=@cid",
            characterId, 0, 255, cancellationToken).ConfigureAwait(false);
        var init = await ReadIntsAsync(connection, transaction,
            "SELECT champion_break_key_id, champion_break_mode, champion_break_value FROM character_init_flags WHERE character_id=@cid",
            characterId, 3, cancellationToken).ConfigureAwait(false);
        var gold = await ReadPairOrDefaultAsync(connection, transaction,
            "SELECT gold_carry_limit, auction_gold_limit FROM character_gold_limits WHERE character_id=@cid",
            characterId, 0, 0, cancellationToken).ConfigureAwait(false);
        var goldLevel = ResolveGoldUpgradeLevel(gold.Item1, gold.Item2);

        var groups = new List<CharacterDailyChallengeStorageGroup>();
        var groupRows = new List<(int Index, int Id)>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT group_index, group_id FROM character_daily_challenge_groups WHERE character_id=@cid ORDER BY group_index", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                groupRows.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }
        var claimed = (await ReadIntListAsync(connection, transaction,
            "SELECT group_index FROM character_daily_challenge_claims WHERE character_id=@cid ORDER BY group_index",
            characterId, cancellationToken).ConfigureAwait(false)).ToHashSet();
        foreach (var group in groupRows)
        {
            if (group.Index is < 0 or >= 6 || group.Id < 0)
                throw new InvalidDataException("Daily challenge group is out of range.");
            var entries = new List<(int, int, int, int)>();
            await using var command = CreateCommand(connection, transaction,
                "SELECT entry_index, track_like_id, value_a, value_b FROM character_daily_challenge_entries WHERE character_id=@cid AND group_index=@group ORDER BY entry_index", characterId);
            command.Parameters.AddWithValue("@group", group.Index);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var entry = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
                if (entry.Item1 < 0 || entry.Item2 < 0 || entry.Item3 < 0 || entry.Item4 < 0)
                    throw new InvalidDataException("Daily challenge entry is out of range.");
                entries.Add(entry);
            }
            groups.Add(new CharacterDailyChallengeStorageGroup(
                checked((byte)group.Index), group.Id, entries, claimed.Contains(group.Index)));
        }
        if (claimed.Except(groupRows.Select(value => value.Index)).Any())
            throw new InvalidDataException("Daily challenge claim references a missing group.");

        var tails = await ReadIntListAsync(connection, transaction,
            "SELECT id_value FROM character_daily_challenge_tail_ids WHERE character_id=@cid ORDER BY sort_order",
            characterId, cancellationToken).ConfigureAwait(false);
        var schedules = new List<(int, int, int, int, int)>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT sort_order, param_a, mode_or_state, content_id, param_b FROM character_daily_schedule_states WHERE character_id=@cid ORDER BY sort_order", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                schedules.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        }
        if (schedules.Count == 0)
            schedules.AddRange([(0, 0, 0, 0, 0), (1, 0, 0, 0, 0), (2, 0, 0, 0, 0)]);
        if (schedules.Count != 3 || !schedules.Select(value => value.Item1).SequenceEqual([0, 1, 2]))
            throw new InvalidDataException("Daily schedule must contain exactly slots 0, 1 and 2.");

        var dungeons = new List<(int, int, int)>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT sort_order, dungeon_id, clear_state FROM character_dungeon_permissions WHERE character_id=@cid ORDER BY sort_order", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                if (row.Item2 <= 0 || row.Item3 is < 0 or > byte.MaxValue)
                    throw new InvalidDataException("Dungeon progress is out of range.");
                dungeons.Add(row);
            }
        }
        var claimedRevive = (await ReadScalarIntAsync(connection, transaction,
            "SELECT value FROM character_daily_counters WHERE character_id=@cid AND counter_key='revive_coin_daily_claim'",
            characterId, 0, cancellationToken).ConfigureAwait(false)) > 0;
        var premiumUsage = await ReadScalarIntAsync(connection, transaction,
            "SELECT value FROM character_daily_counters WHERE character_id=@cid AND counter_key='lottery_double_reward_used'",
            characterId, 0, cancellationToken).ConfigureAwait(false);
        if (premiumUsage < 0)
            throw new InvalidDataException("Character premium usage cannot be negative.");
        return new CharacterProgressionStorageRecord(
            level, growth, cubeType, cubeGrade, init[0], init[1], init[2], goldLevel,
            groups, tails, schedules, dungeons,
            [((byte)1, premiumUsage)], claimedRevive);
    }

    private static async ValueTask<CharacterFeatureStorageRecord> LoadFeaturesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var pets = new List<CharacterPetStorageRow>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT sort_order, creature_key, field04, mode_flag, progress_value, mode1_field0a, mode1_field0b, field_after_value, creature_text, tail_flag FROM character_creatures WHERE character_id=@cid ORDER BY sort_order", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = new CharacterPetStorageRow(
                    reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3),
                    reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                    ReadBoundedBlob(reader, 8, CreatureTextMaximumBytes, "creature text", true), reader.GetInt32(9));
                if (row.SortOrder < 0 || row.CreatureKey <= 0 || row.ModeFlag is < 0 or > byte.MaxValue
                    || row.Field04 is < 0 or > byte.MaxValue
                    || row.Mode1Field0A is < 0 or > byte.MaxValue
                    || row.Mode1Field0B is < 0 or > byte.MaxValue
                    || row.FieldAfterValue is < 0 or > byte.MaxValue
                    || row.TailFlag is < 0 or > byte.MaxValue || row.ProgressValue < 0)
                    throw new InvalidDataException("Creature runtime state is out of range.");
                pets.Add(row);
            }
        }
        var titleBooks = new List<CharacterTitleBookStorageRow>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT category, slot_index, item_core FROM character_new_titlebook WHERE character_id=@cid ORDER BY category, slot_index", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var category = reader.GetInt32(0);
                var slot = reader.GetInt32(1);
                var core = ReadBoundedBlob(reader, 2, 82, "title book item core", false);
                if (category is < 0 or > 4 || slot is < 0 or > ushort.MaxValue || core.Length != 82)
                    throw new InvalidDataException("Title book entry is malformed.");
                if (BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1)) <= 0)
                    throw new InvalidDataException("Title book item ID must be positive.");
                titleBooks.Add(new CharacterTitleBookStorageRow(checked((byte)category), slot, core));
            }
        }
        var achievements = new List<CharacterAchievementStorageRow>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT sort_order, achievement_id, p1, p2, p3, p4 FROM character_achievement_complete WHERE character_id=@cid ORDER BY sort_order", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = new CharacterAchievementStorageRow(
                    reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5));
                if (row.SortOrder < 0 || row.AchievementId <= 0
                    || new[] { row.P1, row.P2, row.P3, row.P4 }.Any(value => value is < 0 or > ushort.MaxValue))
                    throw new InvalidDataException("Achievement state is out of range.");
                achievements.Add(row);
            }
        }
        var collection = new List<(int, int, int)>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT box_index, slot_index, item_id FROM character_collectbox_slots WHERE character_id=@cid ORDER BY box_index, slot_index", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                if (row.Item1 is not (3 or 4) || row.Item2 < 0 || row.Item3 <= 0)
                    throw new InvalidDataException("Collection box state is out of range.");
                collection.Add(row);
            }
        }
        var expertValues = await ReadIntsAsync(connection, transaction, """
            SELECT s.expert_job_type, s.expert_job_exp,
                   COALESCE(e.giveup_count,0), COALESCE(e.disjoint_machine_grade,0),
                   COALESCE(e.disjoint_machine_endurance,0), COALESCE(e.enchanter_endurance,0)
            FROM character_subtype0_fields s
            LEFT JOIN character_expert_job e ON e.character_id=s.character_id
            WHERE s.character_id=@cid
            """, characterId, 6, cancellationToken).ConfigureAwait(false);
        var recipes = await ReadIntListAsync(connection, transaction,
            "SELECT recipe_id FROM character_expert_job_recipes WHERE character_id=@cid ORDER BY recipe_id",
            characterId, cancellationToken).ConfigureAwait(false);
        if (expertValues[0] is < 0 or > byte.MaxValue || expertValues[1] < 0
            || expertValues[2] is < 0 or > ushort.MaxValue
            || expertValues.Skip(3).Any(value => value < 0)
            || recipes.Any(value => value <= 0))
            throw new InvalidDataException("Expert job state is out of range.");
        var mailbox = await LoadMailboxMessagesAsync(connection, transaction, characterId, cancellationToken)
            .ConfigureAwait(false);
        var mailboxTotal = await ReadScalarIntAsync(connection, transaction, """
            SELECT COUNT(DISTINCT m.message_id)
            FROM mailbox_recipients r
            JOIN mailbox_messages m ON m.message_id=r.message_id
            WHERE r.character_id=@cid
              AND r.folder=0
              AND r.saved_flag=0
              AND r.deleted_flag=0
              AND (m.unlimited_flag != 0 OR datetime(m.expire_at) > CURRENT_TIMESTAMP)
            """, characterId, 0, cancellationToken).ConfigureAwait(false);
        var pvp = new List<(int, int, int)>();
        await using (var command = CreateCommand(connection, transaction,
            "SELECT sort_order, mission_id, progress_value FROM character_pvp_missions WHERE character_id=@cid ORDER BY sort_order", characterId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                if (row.Item1 < 0 || row.Item2 <= 0 || row.Item3 < 0)
                    throw new InvalidDataException("PvP mission state is out of range.");
                pvp.Add(row);
            }
        }
        return new CharacterFeatureStorageRecord(
            pets, titleBooks, achievements, collection,
            (expertValues[0], expertValues[1], expertValues[2], expertValues[3],
                expertValues[4], expertValues[5], recipes),
            new CharacterMailboxStorageRecord(mailboxTotal, mailbox), pvp);
    }

    private static async ValueTask<IReadOnlyList<CharacterMailboxMessageStorageRow>> LoadMailboxMessagesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var messages = new List<CharacterMailboxMessageStorageRow>();
        var values = new List<(long Id, int SenderId, string SenderName, int MailType,
            int SourceProtocol, string Title, string Body, int Gold, int Remaining,
            int Created, int State)>();
        await using var command = CreateCommand(connection, transaction, """
            WITH oldest_inbox_page AS (
                SELECT m.message_id, m.created_at
                FROM mailbox_recipients r
                JOIN mailbox_messages m ON m.message_id=r.message_id
                WHERE r.character_id=@cid
                  AND r.folder=0
                  AND r.saved_flag=0
                  AND r.deleted_flag=0
                  AND (m.unlimited_flag != 0 OR datetime(m.expire_at) > CURRENT_TIMESTAMP)
                GROUP BY m.message_id, m.created_at
                ORDER BY datetime(m.created_at) ASC, m.message_id ASC
                LIMIT 20
            )
            SELECT m.message_id, m.sender_character_id, m.sender_name, m.mail_type,
                   m.source_protocol, m.title, m.body,
                   CASE WHEN r.received_gold_flag=0 THEN m.gold ELSE 0 END,
                   CASE WHEN m.unlimited_flag!=0 OR m.expire_at >= '9999-01-01 00:00:00' THEN 0
                        ELSE MIN(2147483647, MAX(0, CAST(strftime('%s',m.expire_at) AS INTEGER)-CAST(strftime('%s','now') AS INTEGER))) END,
                   CAST(strftime('%s',m.created_at) AS INTEGER),
                   CASE WHEN r.saved_flag!=0 THEN 3 WHEN r.read_flag!=0 THEN 2 ELSE 1 END
            FROM oldest_inbox_page p
            JOIN mailbox_messages m ON m.message_id=p.message_id
            JOIN mailbox_recipients r ON r.message_id=m.message_id AND r.character_id=@cid AND r.folder=0
            ORDER BY datetime(p.created_at) DESC, p.message_id DESC
            """, characterId);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                values.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2),
                    reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
                    reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10)));
            }
        }
        foreach (var value in values)
        {
            var attachments = await LoadMailboxAttachmentsAsync(
                connection, transaction, value.Id, cancellationToken).ConfigureAwait(false);
            messages.Add(new CharacterMailboxMessageStorageRow(
                value.Id, value.SenderId, value.SenderName, value.MailType, value.SourceProtocol,
                value.Title, value.Body, value.Gold, value.Remaining, value.Created, value.State,
                attachments));
        }
        return messages;
    }

    private static async ValueTask<IReadOnlyList<CharacterMailboxAttachmentStorageRow>> LoadMailboxAttachmentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long messageId,
        CancellationToken cancellationToken)
    {
        var rows = new List<CharacterMailboxAttachmentStorageRow>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT attachment_id,ordinal,item_type,source_list_type,source_slot_index,
                   source_item_uid,item_template_id,item_kind,item_count,instance_value,
                   durability,seal_flag,option_value,expire_time,marker_16,
                   pet_serial_or_handle,COALESCE(item_core,X''),detail_json
            FROM mailbox_attachments WHERE message_id=@messageId ORDER BY ordinal
            """;
        command.Parameters.AddWithValue("@messageId", messageId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new CharacterMailboxAttachmentStorageRow(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt64(5), reader.GetInt32(6), reader.GetString(7),
                reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11),
                reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15),
                (byte[])reader.GetValue(16), reader.GetString(17)));
        }
        return rows;
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        EnsureCharacterColumns(connection);
        using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        command.ExecuteNonQuery();
        EnsureProjectionColumns(connection);
    }

    private static void EnsureProjectionColumns(SqliteConnection connection)
    {
        EnsureColumns(connection, "character_subtype1_fields",
            [("manage_level", "INTEGER NOT NULL DEFAULT 0")]);
        EnsureColumns(connection, "mailbox_messages",
        [
            ("sender_character_id", "INTEGER NOT NULL DEFAULT 0"),
            ("sender_name", "TEXT NOT NULL DEFAULT ''"),
            ("title", "TEXT NOT NULL DEFAULT ''"),
            ("body", "TEXT NOT NULL DEFAULT ''"),
            ("gold", "INTEGER NOT NULL DEFAULT 0"),
            ("mail_type", "INTEGER NOT NULL DEFAULT 0"),
            ("source_protocol", "INTEGER NOT NULL DEFAULT 0"),
        ]);
        EnsureColumns(connection, "mailbox_recipients",
        [
            ("read_flag", "INTEGER NOT NULL DEFAULT 0"),
            ("received_gold_flag", "INTEGER NOT NULL DEFAULT 0"),
        ]);
    }

    private static void EnsureColumns(
        SqliteConnection connection,
        string table,
        IReadOnlyList<(string Name, string Definition)> required)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var inspect = connection.CreateCommand())
        {
            inspect.CommandText = $"PRAGMA table_info('{table}');";
            using var reader = inspect.ExecuteReader();
            while (reader.Read()) existing.Add(reader.GetString(1));
        }

        foreach (var (name, definition) in required)
        {
            if (existing.Contains(name)) continue;
            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {name} {definition};";
            alter.ExecuteNonQuery();
        }
    }

    private static void EnsureCharacterColumns(SqliteConnection connection)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(characters);";
            using var reader = command.ExecuteReader();
            while (reader.Read()) columns.Add(reader.GetString(1));
        }
        if (columns.Count == 0)
        {
            using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE characters(
                    character_id INTEGER PRIMARY KEY, account_id INTEGER NOT NULL,
                    name TEXT NOT NULL, job INTEGER NOT NULL DEFAULT 0,
                    level INTEGER NOT NULL DEFAULT 1, clone_title_item_id INTEGER NOT NULL DEFAULT 0,
                    appearance_blob BLOB, delete_flag INTEGER NOT NULL DEFAULT 0,
                    bonus_sp INTEGER NOT NULL DEFAULT 0, bonus_tp INTEGER NOT NULL DEFAULT 0,
                    exp INTEGER NOT NULL DEFAULT 0, ex_equip_slot_stat INTEGER NOT NULL DEFAULT 0,
                    pvp_grade INTEGER NOT NULL DEFAULT 0, pvp_rating_grade INTEGER NOT NULL DEFAULT 0,
                    user_state INTEGER NOT NULL DEFAULT 0);
                """;
            create.ExecuteNonQuery();
            return;
        }
        foreach (var (name, definition) in new[]
        {
            ("appearance_blob", "BLOB"), ("clone_title_item_id", "INTEGER NOT NULL DEFAULT 0"),
            ("delete_flag", "INTEGER NOT NULL DEFAULT 0"), ("job", "INTEGER NOT NULL DEFAULT 0"),
            ("level", "INTEGER NOT NULL DEFAULT 1"),
            ("bonus_sp", "INTEGER NOT NULL DEFAULT 0"), ("bonus_tp", "INTEGER NOT NULL DEFAULT 0"),
            ("exp", "INTEGER NOT NULL DEFAULT 0"),
            ("ex_equip_slot_stat", "INTEGER NOT NULL DEFAULT 0"),
            ("pvp_grade", "INTEGER NOT NULL DEFAULT 0"),
            ("pvp_rating_grade", "INTEGER NOT NULL DEFAULT 0"),
            ("user_state", "INTEGER NOT NULL DEFAULT 0"),
        })
        {
            if (columns.Contains(name)) continue;
            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE characters ADD COLUMN {name} {definition};";
            alter.ExecuteNonQuery();
        }
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection, SqliteTransaction transaction, string text, CharacterId id)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = text;
        command.Parameters.AddWithValue("@cid", id.Value);
        return command;
    }

    private static byte[] ReadBoundedBlob(
        SqliteDataReader reader, int ordinal, int maximum, string name, bool allowNull)
    {
        if (reader.IsDBNull(ordinal))
        {
            if (allowNull) return [];
            throw new InvalidDataException($"Character {name} is unavailable.");
        }
        var value = (byte[])reader.GetValue(ordinal);
        if (value.Length > maximum)
            throw new InvalidDataException($"Character {name} exceeds {maximum} bytes.");
        return value;
    }

    private static byte[]? ReadOptionalBoundedBlob(
        SqliteDataReader reader, int ordinal, int maximum, string name)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var value = (byte[])reader.GetValue(ordinal);
        if (value.Length > maximum)
            throw new InvalidDataException($"Character {name} exceeds {maximum} bytes.");
        return value;
    }

    private static void ValidateProfile(
        CharacterSubtype0StorageRecord s0, CharacterSubtype1StorageRecord s1)
    {
        if (s0.Stamina is < 0 or > byte.MaxValue || s0.FatiguePenalty is < 0 or > uint.MaxValue
            || s0.ExpertJobType is < 0 or > byte.MaxValue || s0.ExpertJobExperience is < 0 or > uint.MaxValue
            || s0.HardcoreDeathCount is < 0 or > ushort.MaxValue || s0.UserStateBits is < 0 or > uint.MaxValue
            || s0.ChatBanEndTime is < 0 or > uint.MaxValue || s0.MoodValue is < 0 or > ushort.MaxValue
            || s0.PvpRankPoint is < 0 or > uint.MaxValue
            || new[] { s0.EventCharacter, s0.PrivateStore, s0.HardcoreMode, s0.HardcoreDead }
                .Any(value => value is < 0 or > 1)
            || new[] { s0.PremiumPcRoom, s0.Disguised, s0.ReturnUser, s0.LinkSlotEnabled }
                .Any(value => value is < 0 or > 1)
            || (s0.CreatureBuffer?.Length ?? 0) is not (0 or 8)
            || new[] { s0.CreatureField1, s0.CreatureField2, s0.CreatureField3, s0.CreatureField4,
                s0.ServerGroupId, s0.GuildLevel, s0.DisguiseKind, s0.ReturnUserFlag, s0.ChannelType,
                s0.LinkTypeA, s0.LinkTypeB, s0.ActionByte, s0.CostumeFlag, s0.AuraFlag,
                s0.PetDisplayFlag, s0.TitleDisplayFlag, s0.PvpWinStreak, s0.PvpLoseStreak,
                s0.TrailingByte }.Any(value => value is < 0 or > byte.MaxValue)
            || new[] { s0.FatigueUpdate, s0.ChannelDisplayMode, s0.EmotionIndex,
                s0.FatigueDisplayUpdate }.Any(value => value is < 0 or > ushort.MaxValue)
            || s0.PcRoomId is < 0 or > uint.MaxValue || s0.BlackCount is < 0 or > uint.MaxValue
            || s0.ChaosPoint is < 0 or > uint.MaxValue || s0.PvpStatA is < 0 or > uint.MaxValue
            || new[] { s1.HpMaximum, s1.MpMaximum, s1.PhysicalAttack, s1.PhysicalDefense,
                s1.MagicalAttack, s1.MagicalDefense, s1.MoveSpeed, s1.AttackSpeed,
                s1.CastSpeed, s1.InventoryLimit, s1.Progress1, s1.Progress2 }.Any(value => value < 0)
            || s1.SkillTreeIndex is < -1 or > 1)
            throw new InvalidDataException("Character public profile state is out of range.");
        if (new[] { s1.FireResistance, s1.WaterResistance, s1.DarkResistance, s1.LightResistance }
                .Any(value => value is < short.MinValue or > short.MaxValue)
            || new[] { s1.HpRegenSpeed, s1.MpRegenSpeed, s1.HitRecovery, s1.JumpPower,
                s1.QuestShopCount }.Any(value => value is < 0 or > ushort.MaxValue)
            || new[] { s1.StatLevel, s1.EquippedCreatureLevel, s1.FlagByte }
                .Any(value => value is < 0 or > byte.MaxValue)
            || s1.ManageLevel is < 0 or > byte.MaxValue
            || s1.Weight is < 0 or > uint.MaxValue || s1.NameTagItemId is < 0 or > uint.MaxValue
            || s1.NameTagExpireTime is < 0 or > uint.MaxValue
            || s1.GuildPowerWar is < 0 or > uint.MaxValue
            || s1.ServerTimestamp is < 0 or > uint.MaxValue)
            throw new InvalidDataException("Character extended detail state is out of range.");
    }

    private static async ValueTask<IReadOnlyList<int>> ReadIntListAsync(
        SqliteConnection c, SqliteTransaction t, string sql, CharacterId id, CancellationToken token)
    {
        var values = new List<int>();
        await using var command = CreateCommand(c, t, sql, id);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) values.Add(reader.GetInt32(0));
        return values;
    }

    private static async ValueTask<IReadOnlyList<long>> ReadLongListAsync(
        SqliteConnection c, SqliteTransaction t, string sql, CharacterId id, CancellationToken token)
    {
        var values = new List<long>();
        await using var command = CreateCommand(c, t, sql, id);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) values.Add(reader.GetInt64(0));
        return values;
    }

    private static async ValueTask<int[]> ReadIntsAsync(
        SqliteConnection c, SqliteTransaction t, string sql, CharacterId id, int count, CancellationToken token)
    {
        await using var command = CreateCommand(c, t, sql, id);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidDataException("Required character state is unavailable.");
        var values = new int[count];
        for (var i = 0; i < count; i++) values[i] = reader.GetInt32(i);
        return values;
    }

    private static async ValueTask<int[]> ReadIntsOrDefaultAsync(
        SqliteConnection c, SqliteTransaction t, string sql, CharacterId id, int count, CancellationToken token)
    {
        await using var command = CreateCommand(c, t, sql, id);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return new int[count];
        var values = new int[count];
        for (var i = 0; i < count; i++) values[i] = reader.GetInt32(i);
        return values;
    }

    private static async ValueTask<(int, int)> ReadPairOrDefaultAsync(
        SqliteConnection c, SqliteTransaction t, string sql, CharacterId id,
        int firstDefault, int secondDefault, CancellationToken token)
    {
        await using var command = CreateCommand(c, t, sql, id);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false)
            ? (reader.GetInt32(0), reader.GetInt32(1))
            : (firstDefault, secondDefault);
    }

    private static async ValueTask<int> ReadScalarIntAsync(
        SqliteConnection c, SqliteTransaction t, string sql, CharacterId id,
        int fallback, CancellationToken token)
    {
        await using var command = CreateCommand(c, t, sql, id);
        var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        return value is null or DBNull ? fallback : Convert.ToInt32(value);
    }

    private static int ResolveGoldUpgradeLevel(int carry, int auction)
    {
        var value = Math.Min(carry, auction);
        if (value >= 800_000_000) return 4;
        if (value >= 700_000_000) return 3;
        if (value >= 600_000_000) return 2;
        if (value >= 500_000_000) return 1;
        return 0;
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS character_subtype0_fields(
            character_id INTEGER PRIMARY KEY, stamina INTEGER NOT NULL DEFAULT 0,
            fatigue_penalty INTEGER NOT NULL DEFAULT 0, is_event_character INTEGER NOT NULL DEFAULT 0,
            is_private_store INTEGER NOT NULL DEFAULT 0, expert_job_type INTEGER NOT NULL DEFAULT 0,
            expert_job_exp INTEGER NOT NULL DEFAULT 0, is_hardcore_mode INTEGER NOT NULL DEFAULT 0,
            is_hardcore_dead INTEGER NOT NULL DEFAULT 0, hardcore_death_count INTEGER NOT NULL DEFAULT 0,
            user_state_bits INTEGER NOT NULL DEFAULT 3, chat_ban_end_time INTEGER NOT NULL DEFAULT 0,
            mood_value INTEGER NOT NULL DEFAULT 0, pvp_rank_point INTEGER NOT NULL DEFAULT 0,
            creature_field1 INTEGER NOT NULL DEFAULT 0, creature_field2 INTEGER NOT NULL DEFAULT 0,
            creature_field3 INTEGER NOT NULL DEFAULT 0, creature_field4 INTEGER NOT NULL DEFAULT 0,
            creature_buffer BLOB, pc_room_id INTEGER NOT NULL DEFAULT 65537,
            is_premium_pc_room INTEGER NOT NULL DEFAULT 0, server_group_id INTEGER NOT NULL DEFAULT 0,
            black_count INTEGER NOT NULL DEFAULT 0, guild_level INTEGER NOT NULL DEFAULT 0,
            chaos_point INTEGER NOT NULL DEFAULT 0, disguise_kind INTEGER NOT NULL DEFAULT 0,
            is_disguised INTEGER NOT NULL DEFAULT 0, fatigue_update INTEGER NOT NULL DEFAULT 0,
            return_user_flag INTEGER NOT NULL DEFAULT 1, channel_display_mode INTEGER NOT NULL DEFAULT 0,
            channel_type INTEGER NOT NULL DEFAULT 0, is_return_user INTEGER NOT NULL DEFAULT 0,
            link_slot_enabled INTEGER NOT NULL DEFAULT 0, link_type_a INTEGER NOT NULL DEFAULT 0,
            link_type_b INTEGER NOT NULL DEFAULT 0, emotion_index INTEGER NOT NULL DEFAULT 0,
            action_byte INTEGER NOT NULL DEFAULT 0, fatigue_display_update INTEGER NOT NULL DEFAULT 0,
            costume_flag INTEGER NOT NULL DEFAULT 0, aura_flag INTEGER NOT NULL DEFAULT 0,
            pet_display_flag INTEGER NOT NULL DEFAULT 0, title_display_flag INTEGER NOT NULL DEFAULT 0,
            pvp_stat_a INTEGER NOT NULL DEFAULT 0, pvp_win_streak INTEGER NOT NULL DEFAULT 0,
            pvp_lose_streak INTEGER NOT NULL DEFAULT 0, trailing_byte INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS character_subtype1_fields(
            character_id INTEGER PRIMARY KEY, stat_hp_max INTEGER NOT NULL DEFAULT 0,
            stat_mp_max INTEGER NOT NULL DEFAULT 0, stat_physical_attack INTEGER NOT NULL DEFAULT 0,
            stat_physical_defense INTEGER NOT NULL DEFAULT 0, stat_magical_attack INTEGER NOT NULL DEFAULT 0,
            stat_magical_defense INTEGER NOT NULL DEFAULT 0, stat_move_speed INTEGER NOT NULL DEFAULT 0,
            stat_attack_speed INTEGER NOT NULL DEFAULT 0, stat_cast_speed INTEGER NOT NULL DEFAULT 0,
            stat_inventory_limit INTEGER NOT NULL DEFAULT 0, skill_tree_index INTEGER NOT NULL DEFAULT -1,
            progress1 INTEGER NOT NULL DEFAULT 0, progress2 INTEGER NOT NULL DEFAULT 0,
            stat_fire_resistance INTEGER NOT NULL DEFAULT 0, stat_water_resistance INTEGER NOT NULL DEFAULT 0,
            stat_dark_resistance INTEGER NOT NULL DEFAULT 0, stat_light_resistance INTEGER NOT NULL DEFAULT 0,
            stat_hp_regen_speed INTEGER NOT NULL DEFAULT 0, stat_mp_regen_speed INTEGER NOT NULL DEFAULT 0,
            stat_hit_recovery INTEGER NOT NULL DEFAULT 0, stat_jump_power INTEGER NOT NULL DEFAULT 0,
            stat_weight INTEGER NOT NULL DEFAULT 0, stat_level INTEGER NOT NULL DEFAULT 0,
            name_tag_item_id INTEGER NOT NULL DEFAULT 0, name_tag_expire_time INTEGER NOT NULL DEFAULT 0,
            equipped_creature_level INTEGER NOT NULL DEFAULT 0, equip_list_trailing INTEGER NOT NULL DEFAULT 0,
            flag_byte INTEGER NOT NULL DEFAULT 0, guild_power_war INTEGER NOT NULL DEFAULT 0,
            server_timestamp INTEGER NOT NULL DEFAULT 0, quest_shop_count INTEGER NOT NULL DEFAULT 0,
            manage_level INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS character_init_flags(
            character_id INTEGER PRIMARY KEY, champion_break_key_id INTEGER NOT NULL DEFAULT 0,
            champion_break_mode INTEGER NOT NULL DEFAULT 0, champion_break_value INTEGER NOT NULL DEFAULT 0,
            character_option_blob BLOB);
        CREATE TABLE IF NOT EXISTS character_hotkey_slots(character_id INTEGER, slot_index INTEGER, hotkey_value INTEGER, PRIMARY KEY(character_id,slot_index));
        CREATE TABLE IF NOT EXISTS character_skills(character_id INTEGER, page_index INTEGER, slot INTEGER, skill_id INTEGER, level INTEGER, extra_values BLOB, PRIMARY KEY(character_id,page_index,slot));
        CREATE TABLE IF NOT EXISTS character_dark_knight_combo_skill_pages(character_id INTEGER, page_index INTEGER, body BLOB NOT NULL, PRIMARY KEY(character_id,page_index));
        CREATE TABLE IF NOT EXISTS character_knight_shield_deck(character_id INTEGER, slot_index INTEGER, shield_item_id INTEGER, PRIMARY KEY(character_id,slot_index));
        CREATE TABLE IF NOT EXISTS character_active_quests(character_id INTEGER, slot INTEGER, quest_id INTEGER, trigger_value INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(character_id,slot));
        CREATE TABLE IF NOT EXISTS character_quest_notify_selections(character_id INTEGER, slot_index INTEGER, quest_id INTEGER, PRIMARY KEY(character_id,slot_index));
        CREATE TABLE IF NOT EXISTS character_invisible_falgs(character_id INTEGER, slot_index INTEGER, flag_value INTEGER, PRIMARY KEY(character_id,slot_index));
        CREATE TABLE IF NOT EXISTS character_growth_weapon_stages(character_id INTEGER, sort_order INTEGER, stage_id INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_crystal_contract(character_id INTEGER PRIMARY KEY, cube_type INTEGER NOT NULL DEFAULT 0, cube_grade INTEGER NOT NULL DEFAULT 255);
        CREATE TABLE IF NOT EXISTS character_gold_limits(character_id INTEGER PRIMARY KEY, gold_carry_limit INTEGER NOT NULL, auction_gold_limit INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS character_daily_challenge_groups(character_id INTEGER, group_index INTEGER, group_id INTEGER, PRIMARY KEY(character_id,group_index));
        CREATE TABLE IF NOT EXISTS character_daily_challenge_entries(character_id INTEGER, group_index INTEGER, entry_index INTEGER, track_like_id INTEGER, value_a INTEGER, value_b INTEGER, PRIMARY KEY(character_id,group_index,entry_index));
        CREATE TABLE IF NOT EXISTS character_daily_challenge_claims(character_id INTEGER, group_index INTEGER, claimed_at TEXT, PRIMARY KEY(character_id,group_index));
        CREATE TABLE IF NOT EXISTS character_daily_challenge_tail_ids(character_id INTEGER, sort_order INTEGER, id_value INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_daily_schedule_states(character_id INTEGER, sort_order INTEGER, param_a INTEGER, mode_or_state INTEGER, content_id INTEGER, param_b INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_dungeon_permissions(character_id INTEGER, sort_order INTEGER, dungeon_id INTEGER, clear_state INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_daily_counters(character_id INTEGER, counter_key TEXT, period TEXT, value INTEGER, PRIMARY KEY(character_id,counter_key));
        CREATE TABLE IF NOT EXISTS character_creatures(character_id INTEGER, sort_order INTEGER, creature_key INTEGER, field04 INTEGER, mode_flag INTEGER, progress_value INTEGER, mode1_field0a INTEGER, mode1_field0b INTEGER, field_after_value INTEGER, creature_text BLOB, tail_flag INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_new_titlebook(character_id INTEGER, category INTEGER, slot_index INTEGER, item_core BLOB NOT NULL, PRIMARY KEY(character_id,category,slot_index));
        CREATE TABLE IF NOT EXISTS character_achievement_complete(character_id INTEGER, sort_order INTEGER, achievement_id INTEGER, p1 INTEGER, p2 INTEGER, p3 INTEGER, p4 INTEGER, PRIMARY KEY(character_id,achievement_id));
        CREATE TABLE IF NOT EXISTS character_collectbox_slots(character_id INTEGER, box_index INTEGER, slot_index INTEGER, item_id INTEGER, PRIMARY KEY(character_id,box_index,slot_index));
        CREATE TABLE IF NOT EXISTS character_expert_job(character_id INTEGER PRIMARY KEY, giveup_count INTEGER, disjoint_machine_grade INTEGER, disjoint_machine_endurance INTEGER, enchanter_endurance INTEGER);
        CREATE TABLE IF NOT EXISTS character_expert_job_recipes(character_id INTEGER, recipe_id INTEGER, PRIMARY KEY(character_id,recipe_id));
        CREATE TABLE IF NOT EXISTS mailbox_messages(
            message_id INTEGER PRIMARY KEY AUTOINCREMENT,
            sender_character_id INTEGER NOT NULL DEFAULT 0,
            sender_name TEXT NOT NULL DEFAULT '',
            receiver_character_id INTEGER NOT NULL,
            title TEXT NOT NULL DEFAULT '', body TEXT NOT NULL DEFAULT '', gold INTEGER NOT NULL DEFAULT 0,
            mail_type INTEGER NOT NULL DEFAULT 0, source_protocol INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            unlimited_flag INTEGER NOT NULL DEFAULT 0,
            expire_at TEXT NOT NULL DEFAULT '9999-12-31 23:59:59',
            deleted_by_sender INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS mailbox_recipients(
            recipient_id INTEGER PRIMARY KEY AUTOINCREMENT,
            message_id INTEGER NOT NULL,
            character_id INTEGER NOT NULL,
            folder INTEGER NOT NULL DEFAULT 0,
            read_flag INTEGER NOT NULL DEFAULT 0,
            saved_flag INTEGER NOT NULL DEFAULT 0,
            deleted_flag INTEGER NOT NULL DEFAULT 0,
            received_gold_flag INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(message_id,character_id,folder));
        CREATE TABLE IF NOT EXISTS mailbox_attachments(
            attachment_id INTEGER PRIMARY KEY AUTOINCREMENT, message_id INTEGER NOT NULL,
            ordinal INTEGER NOT NULL DEFAULT 0, item_type INTEGER NOT NULL DEFAULT 0,
            source_list_type INTEGER NOT NULL DEFAULT 0, source_slot_index INTEGER NOT NULL DEFAULT 0,
            source_item_uid INTEGER NOT NULL DEFAULT 0, item_template_id INTEGER NOT NULL,
            item_kind TEXT NOT NULL DEFAULT 'unknown', item_count INTEGER NOT NULL DEFAULT 1,
            instance_value INTEGER NOT NULL DEFAULT 0, durability INTEGER NOT NULL DEFAULT 0,
            seal_flag INTEGER NOT NULL DEFAULT 0, option_value INTEGER NOT NULL DEFAULT 0,
            expire_time INTEGER NOT NULL DEFAULT 0, marker_16 INTEGER NOT NULL DEFAULT -1,
            pet_serial_or_handle INTEGER NOT NULL DEFAULT 0, item_core BLOB,
            detail_json TEXT NOT NULL DEFAULT '');
        CREATE TABLE IF NOT EXISTS character_pvp_missions(character_id INTEGER, sort_order INTEGER, mission_id INTEGER, progress_value INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_dimensions(character_id INTEGER, sort_order INTEGER, dim_key INTEGER, val1 INTEGER, val2 INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_dimension_flags(character_id INTEGER PRIMARY KEY, flag1 INTEGER, flag2 INTEGER, flag3 INTEGER, flag4 INTEGER);
        """;
}
