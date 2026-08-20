using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Schema;

/// <summary>
/// The one place the character database's shape is declared.
/// </summary>
/// <remarks>
/// Before this, every repository declared the tables it happened to read, several declared the
/// same table differently, and <c>CREATE TABLE IF NOT EXISTS</c> meant the first declaration to
/// run won while the rest were silently ignored. The shape of the database was therefore decided
/// by which components a given run happened to build first -- and one order left it broken
/// outright, because a partial declaration of <c>characters</c> could win and then fail the index
/// that names a column it omits.
///
/// What is written below is the shape that arrangement actually produced, read back out of a
/// database built the way the composition root builds one. It is a transcription, not a redesign:
/// every column, type and default is what the server already had, including the ones that used to
/// arrive by <c>ALTER TABLE</c> after the fact.
///
/// Reconciling columns is kept, because it was never only a workaround for ordering: a database
/// that already holds a table from an earlier shape is brought up to this one. What was scattered
/// across five hand-written patchers is one pass over what is declared here, so a column can no
/// longer be declared in one place and forgotten in another.
///
/// Applying it is idempotent, so any number of components may ask for it and the first to do so
/// settles the shape for all of them. Nothing else declares a table.
///
/// One column is here that reading the shape back could not have found: <c>character_gold_limits</c>
/// was declared in two places with different shapes and, alone among such tables, had no patcher
/// reconciling them -- so the shape a database ended up with was whichever declaration ran first,
/// and the column the other one declared was simply absent. Reading back a database gives what won,
/// not what was meant; this table's declaration is the union of both.
/// </remarks>
internal static class CharacterDatabaseSchema
{
    private static readonly string[] Tables = TableDeclarations
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static readonly string[] Indexes = IndexDeclarations
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static readonly (string Table, string Column, string Declaration)[] Columns =
        [.. ReconcilableColumns
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|'))
            .Select(parts => (parts[0], parts[1], parts[2]))];

    /// <summary>Puts the database in the shape everything here expects, if it is not already.</summary>
    public static void Ensure(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        Ensure(connection);
    }

    /// <summary>The same, on a connection the caller already holds.</summary>
    /// <remarks>
    /// Tables, then the columns a table carried over from an earlier shape is missing, then the
    /// indexes. The middle step has to come before the last: an index names columns, and the very
    /// failure this whole move removes was an index meeting a table that did not have them yet.
    /// </remarks>
    public static void Ensure(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Apply(connection, Tables);
        Reconcile(connection);
        Apply(connection, Indexes);
    }

    private static void Apply(SqliteConnection connection, string[] statements)
    {
        foreach (var statement in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Brings a table that already existed in an earlier shape up to the one declared here.
    /// </summary>
    /// <remarks>
    /// Only columns that can be added to a table that already holds rows are considered: a key, a
    /// uniqueness constraint or a reference cannot be introduced this way, and neither can a
    /// column that forbids nothing without saying what nothing means. Anything left out is a
    /// column a table has to be created with, which is what the declarations above do.
    /// </remarks>
    private static void Reconcile(SqliteConnection connection)
    {
        foreach (var group in Columns.GroupBy(column => column.Table))
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var inspect = connection.CreateCommand())
            {
                inspect.CommandText = $"PRAGMA table_info('{group.Key}');";
                using var reader = inspect.ExecuteReader();
                while (reader.Read())
                {
                    present.Add(reader.GetString(1));
                }
            }

            if (present.Count == 0)
            {
                continue;
            }

            foreach (var (table, column, declaration) in group)
            {
                if (present.Contains(column))
                {
                    continue;
                }

                using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration};";
                alter.ExecuteNonQuery();
            }
        }
    }

    /// <remarks>The order is the one the database reported and carries no meaning.</remarks>
    private const string TableDeclarations = """
        CREATE TABLE IF NOT EXISTS account_cargo_items ( item_uid INTEGER PRIMARY KEY AUTOINCREMENT, account_id INTEGER NOT NULL, slot_index INTEGER NOT NULL, item_template_id INTEGER NOT NULL, item_kind TEXT NOT NULL DEFAULT 'unknown', stack_count INTEGER NOT NULL DEFAULT 0, instance_value INTEGER NOT NULL DEFAULT 0, durability INTEGER NOT NULL DEFAULT 0, seal_flag INTEGER NOT NULL DEFAULT 0, option_value INTEGER NOT NULL DEFAULT 0, expire_time INTEGER NOT NULL DEFAULT 0, marker_16 INTEGER NOT NULL DEFAULT 0, extra_json TEXT NOT NULL DEFAULT '{}', created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP );
        CREATE TABLE IF NOT EXISTS account_cargo_new_items ( item_uid INTEGER PRIMARY KEY AUTOINCREMENT, account_id INTEGER NOT NULL, character_id INTEGER, list_type INTEGER NOT NULL, slot_index INTEGER NOT NULL, item_core BLOB NOT NULL CHECK(length(item_core) = 82), created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, UNIQUE(account_id, slot_index) );
        CREATE TABLE IF NOT EXISTS account_cargo_state ( account_id INTEGER PRIMARY KEY, selection_key INTEGER NOT NULL DEFAULT 0, value32 INTEGER NOT NULL DEFAULT 0, item_count INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP );
        CREATE TABLE IF NOT EXISTS account_increase_chance_lottery_progress(account_id INTEGER NOT NULL,item_template_id INTEGER NOT NULL,reward_index INTEGER NOT NULL,PRIMARY KEY(account_id,item_template_id,reward_index));
        CREATE TABLE IF NOT EXISTS account_premiums(account_id INTEGER NOT NULL,premium_type INTEGER NOT NULL,end_time INTEGER NOT NULL DEFAULT 0,updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,PRIMARY KEY(account_id,premium_type));
        CREATE TABLE IF NOT EXISTS account_settings(account_id INTEGER PRIMARY KEY,main_game_option BLOB,quickchat_bank0 BLOB,quickchat_bank1 BLOB,hotkey_key_type INTEGER NOT NULL DEFAULT 0,hotkey_slots BLOB);
        CREATE TABLE IF NOT EXISTS accounts ( account_id INTEGER PRIMARY KEY AUTOINCREMENT, m_id TEXT NOT NULL UNIQUE, password_hash TEXT NOT NULL DEFAULT '', last_login_ip TEXT NOT NULL DEFAULT '', last_login_at TEXT, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP , cera INTEGER NOT NULL DEFAULT 0, token_cera INTEGER NOT NULL DEFAULT 0, happy_token_cera INTEGER NOT NULL DEFAULT 0, lucky_star INTEGER NOT NULL DEFAULT 0, honor_exp INTEGER NOT NULL DEFAULT 0, growth_capsule_exp INTEGER NOT NULL DEFAULT 0, cube_black INTEGER NOT NULL DEFAULT 0, cube_white INTEGER NOT NULL DEFAULT 0, cube_red INTEGER NOT NULL DEFAULT 0, cube_blue INTEGER NOT NULL DEFAULT 0, cube_clear INTEGER NOT NULL DEFAULT 0, cube_gold INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS character_achievement_complete(character_id INTEGER, sort_order INTEGER, achievement_id INTEGER, p1 INTEGER, p2 INTEGER, p3 INTEGER, p4 INTEGER, PRIMARY KEY(character_id,achievement_id));
        CREATE TABLE IF NOT EXISTS character_active_quests(character_id INTEGER, slot INTEGER, quest_id INTEGER, trigger_value INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(character_id,slot));
        CREATE TABLE IF NOT EXISTS character_avatar_detail ( item_uid INTEGER PRIMARY KEY, owner_id INTEGER NOT NULL DEFAULT 0, character_id INTEGER NOT NULL DEFAULT 0, item_id INTEGER NOT NULL DEFAULT 0, expire_date INTEGER NOT NULL DEFAULT 0, clear_avatar_id INTEGER NOT NULL DEFAULT 0, jewel_socket BLOB NOT NULL CHECK(length(jewel_socket)=30), color1 INTEGER NOT NULL DEFAULT 0, color2 INTEGER NOT NULL DEFAULT 0, delete_date INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS character_buy_restrict_items(character_id INTEGER NOT NULL,sort_order INTEGER NOT NULL,entry_id INTEGER NOT NULL,sentinel_or_value INTEGER NOT NULL,flag INTEGER NOT NULL,PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_collectbox_slots(character_id INTEGER, box_index INTEGER, slot_index INTEGER, item_id INTEGER, PRIMARY KEY(character_id,box_index,slot_index));
        CREATE TABLE IF NOT EXISTS character_container_state ( character_id INTEGER NOT NULL, list_type INTEGER NOT NULL, list_param16 INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (character_id, list_type) );
        CREATE TABLE IF NOT EXISTS character_creatures(character_id INTEGER, sort_order INTEGER, creature_key INTEGER, field04 INTEGER, mode_flag INTEGER, progress_value INTEGER, mode1_field0a INTEGER, mode1_field0b INTEGER, field_after_value INTEGER, creature_text BLOB, tail_flag INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_crystal_contract(character_id INTEGER PRIMARY KEY, cube_type INTEGER NOT NULL DEFAULT 0, cube_grade INTEGER NOT NULL DEFAULT 255);
        CREATE TABLE IF NOT EXISTS character_daily_challenge_claims(character_id INTEGER, group_index INTEGER, claimed_at TEXT, PRIMARY KEY(character_id,group_index));
        CREATE TABLE IF NOT EXISTS character_daily_challenge_entries(character_id INTEGER, group_index INTEGER, entry_index INTEGER, track_like_id INTEGER, value_a INTEGER, value_b INTEGER, PRIMARY KEY(character_id,group_index,entry_index));
        CREATE TABLE IF NOT EXISTS character_daily_challenge_groups(character_id INTEGER, group_index INTEGER, group_id INTEGER, PRIMARY KEY(character_id,group_index));
        CREATE TABLE IF NOT EXISTS character_daily_challenge_tail_ids(character_id INTEGER, sort_order INTEGER, id_value INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_daily_counters(character_id INTEGER, counter_key TEXT, period TEXT, value INTEGER, PRIMARY KEY(character_id,counter_key));
        CREATE TABLE IF NOT EXISTS character_daily_schedule_states(character_id INTEGER, sort_order INTEGER, param_a INTEGER, mode_or_state INTEGER, content_id INTEGER, param_b INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_dark_knight_combo_skill_pages(character_id INTEGER, page_index INTEGER, body BLOB NOT NULL, PRIMARY KEY(character_id,page_index));
        CREATE TABLE IF NOT EXISTS character_dimension_flags(character_id INTEGER PRIMARY KEY, flag1 INTEGER, flag2 INTEGER, flag3 INTEGER, flag4 INTEGER);
        CREATE TABLE IF NOT EXISTS character_dimensions(character_id INTEGER, sort_order INTEGER, dim_key INTEGER, val1 INTEGER, val2 INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_dungeon_permissions(character_id INTEGER, sort_order INTEGER, dungeon_id INTEGER, clear_state INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_expert_job(character_id INTEGER PRIMARY KEY, giveup_count INTEGER, disjoint_machine_grade INTEGER, disjoint_machine_endurance INTEGER, enchanter_endurance INTEGER);
        CREATE TABLE IF NOT EXISTS character_expert_job_recipes(character_id INTEGER, recipe_id INTEGER, PRIMARY KEY(character_id,recipe_id));
        CREATE TABLE IF NOT EXISTS character_gold_limits(character_id INTEGER PRIMARY KEY, gold_carry_limit INTEGER NOT NULL, auction_gold_limit INTEGER NOT NULL, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
        CREATE TABLE IF NOT EXISTS character_growth_weapon_stages(character_id INTEGER, sort_order INTEGER, stage_id INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_hotkey_slots(character_id INTEGER, slot_index INTEGER, hotkey_value INTEGER, PRIMARY KEY(character_id,slot_index));
        CREATE TABLE IF NOT EXISTS character_init_flags(character_id INTEGER PRIMARY KEY,champion_break_key_id INTEGER NOT NULL DEFAULT 0,champion_break_mode INTEGER NOT NULL DEFAULT 0,champion_break_value INTEGER NOT NULL DEFAULT 0,character_option_blob BLOB,pc_room_state INTEGER);
        CREATE TABLE IF NOT EXISTS character_invisible_falgs(character_id INTEGER, slot_index INTEGER, flag_value INTEGER, PRIMARY KEY(character_id,slot_index));
        CREATE TABLE IF NOT EXISTS character_item_locks ( character_id INTEGER NOT NULL, equipment_lock_id INTEGER NOT NULL, inventory_list_type INTEGER NOT NULL, slot INTEGER NOT NULL, state INTEGER NOT NULL, remaining_seconds INTEGER, PRIMARY KEY (character_id, equipment_lock_id) );
        CREATE TABLE IF NOT EXISTS character_item_values ( character_id INTEGER NOT NULL, list_kind TEXT NOT NULL, sort_order INTEGER NOT NULL, item_id INTEGER NOT NULL, value INTEGER NOT NULL, PRIMARY KEY (character_id, list_kind, sort_order) );
        CREATE TABLE IF NOT EXISTS character_items ( item_uid INTEGER PRIMARY KEY AUTOINCREMENT, owner_scope TEXT NOT NULL CHECK (owner_scope IN ('character', 'account')), owner_id INTEGER NOT NULL, character_id INTEGER, list_type INTEGER NOT NULL, slot_index INTEGER NOT NULL, item_template_id INTEGER NOT NULL, item_kind TEXT NOT NULL DEFAULT 'unknown', stack_count INTEGER NOT NULL DEFAULT 0, instance_value INTEGER NOT NULL DEFAULT 0, durability INTEGER NOT NULL DEFAULT 0, seal_flag INTEGER NOT NULL DEFAULT 0, option_value INTEGER NOT NULL DEFAULT 0, equipment_lock_id INTEGER NOT NULL DEFAULT 0, expire_time INTEGER NOT NULL DEFAULT 0, marker_16 INTEGER NOT NULL DEFAULT 0, pet_serial_or_handle INTEGER NOT NULL DEFAULT 0, extra_json TEXT NOT NULL DEFAULT '{}', created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP );
        CREATE TABLE IF NOT EXISTS character_knight_shield_deck(character_id INTEGER, slot_index INTEGER, shield_item_id INTEGER, PRIMARY KEY(character_id,slot_index));
        CREATE TABLE IF NOT EXISTS character_mercenary_support(owner_character_id INTEGER NOT NULL,slot INTEGER NOT NULL,support_character_id INTEGER NOT NULL,skill_id INTEGER NOT NULL,striker_skill_id INTEGER NOT NULL DEFAULT 0,updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,PRIMARY KEY(owner_character_id,slot));
        CREATE TABLE IF NOT EXISTS character_new_items ( item_uid INTEGER PRIMARY KEY AUTOINCREMENT, owner_scope TEXT NOT NULL CHECK (owner_scope IN ('character', 'account')), owner_id INTEGER NOT NULL, character_id INTEGER, list_type INTEGER NOT NULL, slot_index INTEGER NOT NULL, item_core BLOB NOT NULL CHECK(length(item_core) = 82), created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, UNIQUE(owner_scope, owner_id, list_type, slot_index) );
        CREATE TABLE IF NOT EXISTS character_new_titlebook(character_id INTEGER, category INTEGER, slot_index INTEGER, item_core BLOB NOT NULL, PRIMARY KEY(character_id,category,slot_index));
        CREATE TABLE IF NOT EXISTS character_pvp_missions(character_id INTEGER, sort_order INTEGER, mission_id INTEGER, progress_value INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_pvp_skill_state ( character_id INTEGER PRIMARY KEY, initialized_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE );
        CREATE TABLE IF NOT EXISTS character_pvp_skills ( character_id INTEGER NOT NULL, page_index INTEGER NOT NULL CHECK (page_index BETWEEN 0 AND 1), slot INTEGER NOT NULL, skill_id INTEGER NOT NULL, level INTEGER NOT NULL, extra_values BLOB, PRIMARY KEY (character_id, page_index, slot), UNIQUE (character_id, page_index, skill_id), FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE );
        CREATE TABLE IF NOT EXISTS character_quest_notify_selections(character_id INTEGER, slot_index INTEGER, quest_id INTEGER, PRIMARY KEY(character_id,slot_index));
        CREATE TABLE IF NOT EXISTS character_rental_items ( character_id INTEGER NOT NULL, shop_entry_id INTEGER NOT NULL, inventory_template_id INTEGER NOT NULL DEFAULT 0, expire_time INTEGER NOT NULL );
        CREATE TABLE IF NOT EXISTS character_skills(character_id INTEGER, page_index INTEGER, slot INTEGER, skill_id INTEGER, level INTEGER, extra_values BLOB, PRIMARY KEY(character_id,page_index,slot));
        CREATE TABLE IF NOT EXISTS character_subtype0_fields( character_id INTEGER PRIMARY KEY, stamina INTEGER NOT NULL DEFAULT 0, fatigue_penalty INTEGER NOT NULL DEFAULT 0, is_event_character INTEGER NOT NULL DEFAULT 0, is_private_store INTEGER NOT NULL DEFAULT 0, expert_job_type INTEGER NOT NULL DEFAULT 0, expert_job_exp INTEGER NOT NULL DEFAULT 0, is_hardcore_mode INTEGER NOT NULL DEFAULT 0, is_hardcore_dead INTEGER NOT NULL DEFAULT 0, hardcore_death_count INTEGER NOT NULL DEFAULT 0, user_state_bits INTEGER NOT NULL DEFAULT 3, chat_ban_end_time INTEGER NOT NULL DEFAULT 0, mood_value INTEGER NOT NULL DEFAULT 0, pvp_rank_point INTEGER NOT NULL DEFAULT 0, creature_field1 INTEGER NOT NULL DEFAULT 0, creature_field2 INTEGER NOT NULL DEFAULT 0, creature_field3 INTEGER NOT NULL DEFAULT 0, creature_field4 INTEGER NOT NULL DEFAULT 0, creature_buffer BLOB, pc_room_id INTEGER NOT NULL DEFAULT 65537, is_premium_pc_room INTEGER NOT NULL DEFAULT 0, server_group_id INTEGER NOT NULL DEFAULT 0, black_count INTEGER NOT NULL DEFAULT 0, guild_level INTEGER NOT NULL DEFAULT 0, chaos_point INTEGER NOT NULL DEFAULT 0, disguise_kind INTEGER NOT NULL DEFAULT 0, is_disguised INTEGER NOT NULL DEFAULT 0, fatigue_update INTEGER NOT NULL DEFAULT 0, return_user_flag INTEGER NOT NULL DEFAULT 1, channel_display_mode INTEGER NOT NULL DEFAULT 0, channel_type INTEGER NOT NULL DEFAULT 0, is_return_user INTEGER NOT NULL DEFAULT 0, link_slot_enabled INTEGER NOT NULL DEFAULT 0, link_type_a INTEGER NOT NULL DEFAULT 0, link_type_b INTEGER NOT NULL DEFAULT 0, emotion_index INTEGER NOT NULL DEFAULT 0, action_byte INTEGER NOT NULL DEFAULT 0, fatigue_display_update INTEGER NOT NULL DEFAULT 0, costume_flag INTEGER NOT NULL DEFAULT 0, aura_flag INTEGER NOT NULL DEFAULT 0, pet_display_flag INTEGER NOT NULL DEFAULT 0, title_display_flag INTEGER NOT NULL DEFAULT 0, pvp_stat_a INTEGER NOT NULL DEFAULT 0, pvp_win_streak INTEGER NOT NULL DEFAULT 0, pvp_lose_streak INTEGER NOT NULL DEFAULT 0, trailing_byte INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS character_subtype1_fields( character_id INTEGER PRIMARY KEY, stat_hp_max INTEGER NOT NULL DEFAULT 0, stat_mp_max INTEGER NOT NULL DEFAULT 0, stat_physical_attack INTEGER NOT NULL DEFAULT 0, stat_physical_defense INTEGER NOT NULL DEFAULT 0, stat_magical_attack INTEGER NOT NULL DEFAULT 0, stat_magical_defense INTEGER NOT NULL DEFAULT 0, stat_move_speed INTEGER NOT NULL DEFAULT 0, stat_attack_speed INTEGER NOT NULL DEFAULT 0, stat_cast_speed INTEGER NOT NULL DEFAULT 0, stat_inventory_limit INTEGER NOT NULL DEFAULT 0, skill_tree_index INTEGER NOT NULL DEFAULT -1, progress1 INTEGER NOT NULL DEFAULT 0, progress2 INTEGER NOT NULL DEFAULT 0, stat_fire_resistance INTEGER NOT NULL DEFAULT 0, stat_water_resistance INTEGER NOT NULL DEFAULT 0, stat_dark_resistance INTEGER NOT NULL DEFAULT 0, stat_light_resistance INTEGER NOT NULL DEFAULT 0, stat_hp_regen_speed INTEGER NOT NULL DEFAULT 0, stat_mp_regen_speed INTEGER NOT NULL DEFAULT 0, stat_hit_recovery INTEGER NOT NULL DEFAULT 0, stat_jump_power INTEGER NOT NULL DEFAULT 0, stat_weight INTEGER NOT NULL DEFAULT 0, current_hp_mp_percent INTEGER NOT NULL DEFAULT 0, name_tag_item_id INTEGER NOT NULL DEFAULT 0, name_tag_expire_time INTEGER NOT NULL DEFAULT 0, equipped_creature_level INTEGER NOT NULL DEFAULT 0, equip_list_trailing INTEGER NOT NULL DEFAULT 0, flag_byte INTEGER NOT NULL DEFAULT 0, guild_power_war INTEGER NOT NULL DEFAULT 0, server_timestamp INTEGER NOT NULL DEFAULT 0, quest_shop_count INTEGER NOT NULL DEFAULT 0, manage_level INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS characters(character_id INTEGER PRIMARY KEY,account_id INTEGER NOT NULL,name TEXT NOT NULL,name_bytes BLOB,job INTEGER NOT NULL DEFAULT 0,grow_type INTEGER NOT NULL DEFAULT 0,level INTEGER NOT NULL DEFAULT 1,clone_title_item_id INTEGER NOT NULL DEFAULT 0,slot_index INTEGER NOT NULL DEFAULT 0,delete_flag INTEGER NOT NULL DEFAULT 0, town_id INTEGER NOT NULL DEFAULT 0, area_id INTEGER NOT NULL DEFAULT 0, pos_x INTEGER NOT NULL DEFAULT 0, pos_y INTEGER NOT NULL DEFAULT 0, direction INTEGER NOT NULL DEFAULT 5, area_state INTEGER NOT NULL DEFAULT 3, appearance_blob BLOB, bonus_sp INTEGER NOT NULL DEFAULT 0, bonus_tp INTEGER NOT NULL DEFAULT 0, exp INTEGER NOT NULL DEFAULT 0, ex_equip_slot_stat INTEGER NOT NULL DEFAULT 0, pvp_grade INTEGER NOT NULL DEFAULT 0, pvp_rating_grade INTEGER NOT NULL DEFAULT 0, user_state INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS mailbox_attachments( attachment_id INTEGER PRIMARY KEY AUTOINCREMENT, message_id INTEGER NOT NULL, ordinal INTEGER NOT NULL DEFAULT 0, item_type INTEGER NOT NULL DEFAULT 0, source_list_type INTEGER NOT NULL DEFAULT 0, source_slot_index INTEGER NOT NULL DEFAULT 0, source_item_uid INTEGER NOT NULL DEFAULT 0, item_template_id INTEGER NOT NULL, item_kind TEXT NOT NULL DEFAULT 'unknown', item_count INTEGER NOT NULL DEFAULT 1, instance_value INTEGER NOT NULL DEFAULT 0, durability INTEGER NOT NULL DEFAULT 0, seal_flag INTEGER NOT NULL DEFAULT 0, option_value INTEGER NOT NULL DEFAULT 0, expire_time INTEGER NOT NULL DEFAULT 0, marker_16 INTEGER NOT NULL DEFAULT -1, pet_serial_or_handle INTEGER NOT NULL DEFAULT 0, item_core BLOB, detail_json TEXT NOT NULL DEFAULT '');
        CREATE TABLE IF NOT EXISTS mailbox_messages( message_id INTEGER PRIMARY KEY AUTOINCREMENT, sender_character_id INTEGER NOT NULL DEFAULT 0, sender_name TEXT NOT NULL DEFAULT '', receiver_character_id INTEGER NOT NULL, title TEXT NOT NULL DEFAULT '', body TEXT NOT NULL DEFAULT '', gold INTEGER NOT NULL DEFAULT 0, mail_type INTEGER NOT NULL DEFAULT 0, source_protocol INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, unlimited_flag INTEGER NOT NULL DEFAULT 0, expire_at TEXT NOT NULL DEFAULT '9999-12-31 23:59:59', deleted_by_sender INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS mailbox_recipients( recipient_id INTEGER PRIMARY KEY AUTOINCREMENT, message_id INTEGER NOT NULL, character_id INTEGER NOT NULL, folder INTEGER NOT NULL DEFAULT 0, read_flag INTEGER NOT NULL DEFAULT 0, saved_flag INTEGER NOT NULL DEFAULT 0, deleted_flag INTEGER NOT NULL DEFAULT 0, received_gold_flag INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, UNIQUE(message_id,character_id,folder));
        CREATE TABLE IF NOT EXISTS rental_items ( character_id INTEGER NOT NULL, item_id INTEGER NOT NULL, inventory_template_id INTEGER NOT NULL DEFAULT 0, expire_time INTEGER NOT NULL );
        """;

    private const string IndexDeclarations = """
        CREATE INDEX IF NOT EXISTS idx_account_cargo_items_owner ON account_cargo_items(account_id, slot_index);
        CREATE INDEX IF NOT EXISTS idx_character_items_character ON character_items(character_id, list_type, slot_index);
        CREATE INDEX IF NOT EXISTS idx_character_new_items_character_space ON character_new_items(character_id, list_type, slot_index);
        CREATE INDEX IF NOT EXISTS idx_character_rental_items_char ON character_rental_items(character_id);
        CREATE INDEX IF NOT EXISTS idx_characters_account ON characters(account_id, delete_flag, slot_index);
        CREATE INDEX IF NOT EXISTS idx_rental_items_char ON rental_items(character_id);
        """;

    /// <summary>Table, column and declaration, one per line.</summary>
    private const string ReconcilableColumns = """
        account_cargo_items|item_kind|TEXT NOT NULL DEFAULT 'unknown'
        account_cargo_items|stack_count|INTEGER NOT NULL DEFAULT 0
        account_cargo_items|instance_value|INTEGER NOT NULL DEFAULT 0
        account_cargo_items|durability|INTEGER NOT NULL DEFAULT 0
        account_cargo_items|seal_flag|INTEGER NOT NULL DEFAULT 0
        account_cargo_items|option_value|INTEGER NOT NULL DEFAULT 0
        account_cargo_items|expire_time|INTEGER NOT NULL DEFAULT 0
        account_cargo_items|marker_16|INTEGER NOT NULL DEFAULT 0
        account_cargo_items|extra_json|TEXT NOT NULL DEFAULT '{}'
        account_cargo_new_items|character_id|INTEGER
        account_cargo_state|selection_key|INTEGER NOT NULL DEFAULT 0
        account_cargo_state|value32|INTEGER NOT NULL DEFAULT 0
        account_cargo_state|item_count|INTEGER NOT NULL DEFAULT 0
        account_premiums|end_time|INTEGER NOT NULL DEFAULT 0
        account_settings|main_game_option|BLOB
        account_settings|quickchat_bank0|BLOB
        account_settings|quickchat_bank1|BLOB
        account_settings|hotkey_key_type|INTEGER NOT NULL DEFAULT 0
        account_settings|hotkey_slots|BLOB
        accounts|password_hash|TEXT NOT NULL DEFAULT ''
        accounts|last_login_ip|TEXT NOT NULL DEFAULT ''
        accounts|last_login_at|TEXT
        accounts|cera|INTEGER NOT NULL DEFAULT 0
        accounts|token_cera|INTEGER NOT NULL DEFAULT 0
        accounts|happy_token_cera|INTEGER NOT NULL DEFAULT 0
        accounts|lucky_star|INTEGER NOT NULL DEFAULT 0
        accounts|honor_exp|INTEGER NOT NULL DEFAULT 0
        accounts|growth_capsule_exp|INTEGER NOT NULL DEFAULT 0
        accounts|cube_black|INTEGER NOT NULL DEFAULT 0
        accounts|cube_white|INTEGER NOT NULL DEFAULT 0
        accounts|cube_red|INTEGER NOT NULL DEFAULT 0
        accounts|cube_blue|INTEGER NOT NULL DEFAULT 0
        accounts|cube_clear|INTEGER NOT NULL DEFAULT 0
        accounts|cube_gold|INTEGER NOT NULL DEFAULT 0
        character_achievement_complete|character_id|INTEGER
        character_achievement_complete|sort_order|INTEGER
        character_achievement_complete|achievement_id|INTEGER
        character_achievement_complete|p1|INTEGER
        character_achievement_complete|p2|INTEGER
        character_achievement_complete|p3|INTEGER
        character_achievement_complete|p4|INTEGER
        character_active_quests|character_id|INTEGER
        character_active_quests|slot|INTEGER
        character_active_quests|quest_id|INTEGER
        character_active_quests|trigger_value|INTEGER NOT NULL DEFAULT 0
        character_avatar_detail|owner_id|INTEGER NOT NULL DEFAULT 0
        character_avatar_detail|character_id|INTEGER NOT NULL DEFAULT 0
        character_avatar_detail|item_id|INTEGER NOT NULL DEFAULT 0
        character_avatar_detail|expire_date|INTEGER NOT NULL DEFAULT 0
        character_avatar_detail|clear_avatar_id|INTEGER NOT NULL DEFAULT 0
        character_avatar_detail|color1|INTEGER NOT NULL DEFAULT 0
        character_avatar_detail|color2|INTEGER NOT NULL DEFAULT 0
        character_avatar_detail|delete_date|INTEGER NOT NULL DEFAULT 0
        character_collectbox_slots|character_id|INTEGER
        character_collectbox_slots|box_index|INTEGER
        character_collectbox_slots|slot_index|INTEGER
        character_collectbox_slots|item_id|INTEGER
        character_container_state|list_param16|INTEGER NOT NULL DEFAULT 0
        character_creatures|character_id|INTEGER
        character_creatures|sort_order|INTEGER
        character_creatures|creature_key|INTEGER
        character_creatures|field04|INTEGER
        character_creatures|mode_flag|INTEGER
        character_creatures|progress_value|INTEGER
        character_creatures|mode1_field0a|INTEGER
        character_creatures|mode1_field0b|INTEGER
        character_creatures|field_after_value|INTEGER
        character_creatures|creature_text|BLOB
        character_creatures|tail_flag|INTEGER
        character_crystal_contract|cube_type|INTEGER NOT NULL DEFAULT 0
        character_crystal_contract|cube_grade|INTEGER NOT NULL DEFAULT 255
        character_daily_challenge_claims|character_id|INTEGER
        character_daily_challenge_claims|group_index|INTEGER
        character_daily_challenge_claims|claimed_at|TEXT
        character_daily_challenge_entries|character_id|INTEGER
        character_daily_challenge_entries|group_index|INTEGER
        character_daily_challenge_entries|entry_index|INTEGER
        character_daily_challenge_entries|track_like_id|INTEGER
        character_daily_challenge_entries|value_a|INTEGER
        character_daily_challenge_entries|value_b|INTEGER
        character_daily_challenge_groups|character_id|INTEGER
        character_daily_challenge_groups|group_index|INTEGER
        character_daily_challenge_groups|group_id|INTEGER
        character_daily_challenge_tail_ids|character_id|INTEGER
        character_daily_challenge_tail_ids|sort_order|INTEGER
        character_daily_challenge_tail_ids|id_value|INTEGER
        character_daily_counters|character_id|INTEGER
        character_daily_counters|counter_key|TEXT
        character_daily_counters|period|TEXT
        character_daily_counters|value|INTEGER
        character_daily_schedule_states|character_id|INTEGER
        character_daily_schedule_states|sort_order|INTEGER
        character_daily_schedule_states|param_a|INTEGER
        character_daily_schedule_states|mode_or_state|INTEGER
        character_daily_schedule_states|content_id|INTEGER
        character_daily_schedule_states|param_b|INTEGER
        character_dark_knight_combo_skill_pages|character_id|INTEGER
        character_dark_knight_combo_skill_pages|page_index|INTEGER
        character_dimension_flags|flag1|INTEGER
        character_dimension_flags|flag2|INTEGER
        character_dimension_flags|flag3|INTEGER
        character_dimension_flags|flag4|INTEGER
        character_dimensions|character_id|INTEGER
        character_dimensions|sort_order|INTEGER
        character_dimensions|dim_key|INTEGER
        character_dimensions|val1|INTEGER
        character_dimensions|val2|INTEGER
        character_dungeon_permissions|character_id|INTEGER
        character_dungeon_permissions|sort_order|INTEGER
        character_dungeon_permissions|dungeon_id|INTEGER
        character_dungeon_permissions|clear_state|INTEGER
        character_expert_job|giveup_count|INTEGER
        character_expert_job|disjoint_machine_grade|INTEGER
        character_expert_job|disjoint_machine_endurance|INTEGER
        character_expert_job|enchanter_endurance|INTEGER
        character_expert_job_recipes|character_id|INTEGER
        character_expert_job_recipes|recipe_id|INTEGER
        character_growth_weapon_stages|character_id|INTEGER
        character_growth_weapon_stages|sort_order|INTEGER
        character_growth_weapon_stages|stage_id|INTEGER
        character_hotkey_slots|character_id|INTEGER
        character_hotkey_slots|slot_index|INTEGER
        character_hotkey_slots|hotkey_value|INTEGER
        character_init_flags|champion_break_key_id|INTEGER NOT NULL DEFAULT 0
        character_init_flags|champion_break_mode|INTEGER NOT NULL DEFAULT 0
        character_init_flags|champion_break_value|INTEGER NOT NULL DEFAULT 0
        character_init_flags|character_option_blob|BLOB
        character_init_flags|pc_room_state|INTEGER
        character_invisible_falgs|character_id|INTEGER
        character_invisible_falgs|slot_index|INTEGER
        character_invisible_falgs|flag_value|INTEGER
        character_item_locks|remaining_seconds|INTEGER
        character_items|character_id|INTEGER
        character_items|item_kind|TEXT NOT NULL DEFAULT 'unknown'
        character_items|stack_count|INTEGER NOT NULL DEFAULT 0
        character_items|instance_value|INTEGER NOT NULL DEFAULT 0
        character_items|durability|INTEGER NOT NULL DEFAULT 0
        character_items|seal_flag|INTEGER NOT NULL DEFAULT 0
        character_items|option_value|INTEGER NOT NULL DEFAULT 0
        character_items|equipment_lock_id|INTEGER NOT NULL DEFAULT 0
        character_items|expire_time|INTEGER NOT NULL DEFAULT 0
        character_items|marker_16|INTEGER NOT NULL DEFAULT 0
        character_items|pet_serial_or_handle|INTEGER NOT NULL DEFAULT 0
        character_items|extra_json|TEXT NOT NULL DEFAULT '{}'
        character_knight_shield_deck|character_id|INTEGER
        character_knight_shield_deck|slot_index|INTEGER
        character_knight_shield_deck|shield_item_id|INTEGER
        character_mercenary_support|striker_skill_id|INTEGER NOT NULL DEFAULT 0
        character_new_items|character_id|INTEGER
        character_new_titlebook|character_id|INTEGER
        character_new_titlebook|category|INTEGER
        character_new_titlebook|slot_index|INTEGER
        character_pvp_missions|character_id|INTEGER
        character_pvp_missions|sort_order|INTEGER
        character_pvp_missions|mission_id|INTEGER
        character_pvp_missions|progress_value|INTEGER
        character_pvp_skills|extra_values|BLOB
        character_quest_notify_selections|character_id|INTEGER
        character_quest_notify_selections|slot_index|INTEGER
        character_quest_notify_selections|quest_id|INTEGER
        character_rental_items|inventory_template_id|INTEGER NOT NULL DEFAULT 0
        character_skills|character_id|INTEGER
        character_skills|page_index|INTEGER
        character_skills|slot|INTEGER
        character_skills|skill_id|INTEGER
        character_skills|level|INTEGER
        character_skills|extra_values|BLOB
        character_subtype0_fields|stamina|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|fatigue_penalty|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|is_event_character|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|is_private_store|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|expert_job_type|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|expert_job_exp|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|is_hardcore_mode|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|is_hardcore_dead|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|hardcore_death_count|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|user_state_bits|INTEGER NOT NULL DEFAULT 3
        character_subtype0_fields|chat_ban_end_time|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|mood_value|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|pvp_rank_point|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|creature_field1|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|creature_field2|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|creature_field3|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|creature_field4|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|creature_buffer|BLOB
        character_subtype0_fields|pc_room_id|INTEGER NOT NULL DEFAULT 65537
        character_subtype0_fields|is_premium_pc_room|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|server_group_id|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|black_count|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|guild_level|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|chaos_point|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|disguise_kind|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|is_disguised|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|fatigue_update|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|return_user_flag|INTEGER NOT NULL DEFAULT 1
        character_subtype0_fields|channel_display_mode|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|channel_type|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|is_return_user|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|link_slot_enabled|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|link_type_a|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|link_type_b|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|emotion_index|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|action_byte|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|fatigue_display_update|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|costume_flag|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|aura_flag|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|pet_display_flag|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|title_display_flag|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|pvp_stat_a|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|pvp_win_streak|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|pvp_lose_streak|INTEGER NOT NULL DEFAULT 0
        character_subtype0_fields|trailing_byte|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_hp_max|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_mp_max|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_physical_attack|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_physical_defense|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_magical_attack|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_magical_defense|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_move_speed|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_attack_speed|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_cast_speed|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_inventory_limit|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|skill_tree_index|INTEGER NOT NULL DEFAULT -1
        character_subtype1_fields|progress1|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|progress2|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_fire_resistance|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_water_resistance|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_dark_resistance|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_light_resistance|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_hp_regen_speed|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_mp_regen_speed|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_hit_recovery|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_jump_power|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|stat_weight|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|current_hp_mp_percent|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|name_tag_item_id|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|name_tag_expire_time|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|equipped_creature_level|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|equip_list_trailing|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|flag_byte|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|guild_power_war|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|server_timestamp|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|quest_shop_count|INTEGER NOT NULL DEFAULT 0
        character_subtype1_fields|manage_level|INTEGER NOT NULL DEFAULT 0
        characters|name_bytes|BLOB
        characters|job|INTEGER NOT NULL DEFAULT 0
        characters|grow_type|INTEGER NOT NULL DEFAULT 0
        characters|level|INTEGER NOT NULL DEFAULT 1
        characters|clone_title_item_id|INTEGER NOT NULL DEFAULT 0
        characters|slot_index|INTEGER NOT NULL DEFAULT 0
        characters|delete_flag|INTEGER NOT NULL DEFAULT 0
        characters|town_id|INTEGER NOT NULL DEFAULT 0
        characters|area_id|INTEGER NOT NULL DEFAULT 0
        characters|pos_x|INTEGER NOT NULL DEFAULT 0
        characters|pos_y|INTEGER NOT NULL DEFAULT 0
        characters|direction|INTEGER NOT NULL DEFAULT 5
        characters|area_state|INTEGER NOT NULL DEFAULT 3
        characters|appearance_blob|BLOB
        characters|bonus_sp|INTEGER NOT NULL DEFAULT 0
        characters|bonus_tp|INTEGER NOT NULL DEFAULT 0
        characters|exp|INTEGER NOT NULL DEFAULT 0
        characters|ex_equip_slot_stat|INTEGER NOT NULL DEFAULT 0
        characters|pvp_grade|INTEGER NOT NULL DEFAULT 0
        characters|pvp_rating_grade|INTEGER NOT NULL DEFAULT 0
        characters|user_state|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|ordinal|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|item_type|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|source_list_type|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|source_slot_index|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|source_item_uid|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|item_kind|TEXT NOT NULL DEFAULT 'unknown'
        mailbox_attachments|item_count|INTEGER NOT NULL DEFAULT 1
        mailbox_attachments|instance_value|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|durability|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|seal_flag|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|option_value|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|expire_time|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|marker_16|INTEGER NOT NULL DEFAULT -1
        mailbox_attachments|pet_serial_or_handle|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|item_core|BLOB
        mailbox_attachments|detail_json|TEXT NOT NULL DEFAULT ''
        mailbox_messages|sender_character_id|INTEGER NOT NULL DEFAULT 0
        mailbox_messages|sender_name|TEXT NOT NULL DEFAULT ''
        mailbox_messages|title|TEXT NOT NULL DEFAULT ''
        mailbox_messages|body|TEXT NOT NULL DEFAULT ''
        mailbox_messages|gold|INTEGER NOT NULL DEFAULT 0
        mailbox_messages|mail_type|INTEGER NOT NULL DEFAULT 0
        mailbox_messages|source_protocol|INTEGER NOT NULL DEFAULT 0
        mailbox_messages|unlimited_flag|INTEGER NOT NULL DEFAULT 0
        mailbox_messages|expire_at|TEXT NOT NULL DEFAULT '9999-12-31 23:59:59'
        mailbox_messages|deleted_by_sender|INTEGER NOT NULL DEFAULT 0
        mailbox_recipients|folder|INTEGER NOT NULL DEFAULT 0
        mailbox_recipients|read_flag|INTEGER NOT NULL DEFAULT 0
        mailbox_recipients|saved_flag|INTEGER NOT NULL DEFAULT 0
        mailbox_recipients|deleted_flag|INTEGER NOT NULL DEFAULT 0
        mailbox_recipients|received_gold_flag|INTEGER NOT NULL DEFAULT 0
        rental_items|inventory_template_id|INTEGER NOT NULL DEFAULT 0
        """;
}
