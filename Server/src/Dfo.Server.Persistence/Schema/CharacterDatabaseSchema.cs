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
/// One table is deliberately absent from it: <c>character_skills</c>. Its <c>mode</c> column is
/// <c>NOT NULL</c> with no default, and there is no value the reconciler could put in it for rows
/// that predate it -- the whole point of the column is that a row's mode is not derivable from the
/// row. A database carrying the older two-table shape is therefore not reconciled into this one; it
/// is replaced, which is what this stage of the project does with databases anyway.
///
/// One column here could not have been found by reading a database back. <c>character_gold_limits</c>
/// was once declared in two places with different shapes and, alone among such tables, had no
/// patcher reconciling them -- so the shape a database ended up with was whichever declaration ran
/// first, and the column the other one declared was simply absent. Reading back a database gives
/// what won, not what was meant, so the union of both was taken. That table has since been folded
/// into <c>character_stats</c>, which is where its two caps live now; the union survives as those
/// columns.
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
        KeepWritersAndReadersApart(connection);
        RetireRuntimeTables(connection);
        Apply(connection, Tables);
        Reconcile(connection);
        MigrateMailboxGold(connection);
        Apply(connection, Indexes);
    }

    // The old message-level gold is a receivable asset. Keep its original columns for
    // legacy readers, but give it an independent attachment identity exactly once.
    private static void MigrateMailboxGold(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mailbox_attachments
                (message_id, ordinal, item_template_id, asset_kind, gold_amount, claimed_flag)
            SELECT m.message_id, COALESCE((SELECT MAX(a.ordinal) + 1 FROM mailbox_attachments a
                    WHERE a.message_id = m.message_id), 0), 0, 'gold', m.gold,
                   CASE WHEN EXISTS (SELECT 1 FROM mailbox_recipients r
                       WHERE r.message_id=m.message_id AND r.character_id=m.receiver_character_id
                         AND r.received_gold_flag<>0) THEN 1 ELSE 0 END
            FROM mailbox_messages m
            WHERE m.gold > 0 AND NOT EXISTS
                (SELECT 1 FROM mailbox_attachments a
                 WHERE a.message_id=m.message_id AND a.asset_kind='gold');
            """;
        command.ExecuteNonQuery();
    }

    private static void RetireRuntimeTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE IF EXISTS login_tickets;";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// How the database keeps what is being written apart from what is being read.
    /// </summary>
    /// <remarks>
    /// Set here because it is a property of the database file rather than of any connection: once
    /// applied it stays, and this is the one place that decides what the file looks like.
    ///
    /// It is what makes a use case able to read several tables as one question. Under the default
    /// journal, a reader holds the file until it finishes, so anyone wanting to write during a
    /// long read is refused rather than delayed. Here a reader sees the database as it stood when
    /// it began and a writer carries on regardless -- which is the whole point of giving reads a
    /// transaction of their own.
    ///
    /// The answer is read back rather than discarded. Asking for this mode is a request the
    /// database may decline, and a declined one leaves everything working except the guarantee it
    /// was asked for: reads would go back to being able to see a change land in the middle of
    /// them, with nothing anywhere saying so.
    /// </remarks>
    private static void KeepWritersAndReadersApart(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        var mode = command.ExecuteScalar() as string;
        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"This database answered '{mode}' when asked to keep writers and readers apart, "
                + "so a use case reading several tables could not see one moment.");
        }
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

    /// <remarks>
    /// The order is the one the database reported and carries no meaning.
    ///
    /// <c>item_slots</c> is the one declaration here that was not read back out of a database,
    /// because no database has ever had it. It says where a thing is -- which owner's which
    /// container's which position, holding what, and how many -- and it says it the same way for
    /// a thing that has an identity of its own and for a pile that has none. Which of the two a
    /// row is saying is <c>kind</c>: <c>world</c> means <c>id</c> names a row of <c>items</c> and
    /// there is exactly one of it, <c>template</c> means <c>id</c> names an archive template and
    /// <c>count</c> says how many.
    ///
    /// <c>core_uid</c> is beside them because a pile keeps a row of <c>items</c> too -- the
    /// eighty-two bytes it is sent to the client as are not reconstructible from a template number
    /// and a count -- and <c>id</c> for a pile names the template rather than that row. So the
    /// position names its row outright: for a thing with an identity the two are the same number,
    /// which the table holds them to, and for a pile <c>core_uid</c> is the only thing that says
    /// which row is the pile's. It is what makes a row nothing points at answerable in one
    /// condition, and what lets a position be emptied without a position column to find its row by.
    ///
    /// <b><c>items</c> no longer says where anything is.</b> The four columns that used to --
    /// <c>owner_scope</c>, <c>owner_id</c>, <c>list_type</c>, <c>slot_index</c> -- are gone, and so
    /// is the uniqueness over them. A thing's row is what it is; where it is, is a row of
    /// <c>item_slots</c> naming it, and there is exactly one place that answers that question now.
    ///
    /// <c>items</c> grew the eight columns a contract creature carries beyond its core, named the
    /// way the avatar's are: the row is the thing, and what a thing of that kind carries are
    /// columns of it. They are absent on every row that is not one.
    ///
    /// <c>item_uid</c> is now bounded. It is the value a creature's identity goes on the wire as,
    /// and that field is a signed 32-bit integer, so a database able to hand out a larger one
    /// would be handing out an identity the server cannot say. The bound is on the column that
    /// allocates it rather than on a caller, because allocation is the only place one is made.
    /// </remarks>
    private const string TableDeclarations = """
        CREATE TABLE IF NOT EXISTS gm_administrators (id INTEGER PRIMARY KEY AUTOINCREMENT,login_name TEXT NOT NULL UNIQUE,password_record TEXT NOT NULL,enabled INTEGER NOT NULL,permissions INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS gm_sessions (token_hash TEXT PRIMARY KEY,administrator_id INTEGER NOT NULL,expires_at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS gm_audit (operation_id TEXT PRIMARY KEY,occurred_at INTEGER NOT NULL,action TEXT NOT NULL,administrator_id INTEGER,administrator_name TEXT,target TEXT NOT NULL,outcome TEXT NOT NULL,reason TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS gm_mail_drafts (administrator_id INTEGER PRIMARY KEY,document TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS gm_mail_templates (administrator_id INTEGER NOT NULL,id TEXT NOT NULL,name TEXT NOT NULL,document TEXT NOT NULL,PRIMARY KEY(administrator_id,id),UNIQUE(administrator_id,name));
        CREATE TABLE IF NOT EXISTS gm_mail_operations (administrator_id INTEGER NOT NULL,request_id TEXT NOT NULL,payload_hash TEXT NOT NULL,committed_at INTEGER NOT NULL,receipt TEXT NOT NULL,PRIMARY KEY(administrator_id,request_id));
        CREATE TABLE IF NOT EXISTS notice_states (
            owner_type INTEGER NOT NULL CHECK(owner_type IN (0,1)),
            owner_id INTEGER NOT NULL CHECK(owner_id > 0),
            notice_group INTEGER NOT NULL CHECK(notice_group IN (0,1)),
            notice_id INTEGER NOT NULL CHECK(notice_id BETWEEN 0 AND 127),
            PRIMARY KEY(owner_type,owner_id,notice_group,notice_id)
        );
        CREATE TABLE IF NOT EXISTS npc_shop_purchase_counts (
            owner_scope TEXT NOT NULL CHECK(owner_scope IN ('account', 'character')),
            owner_id INTEGER NOT NULL CHECK(owner_id > 0),
            npc_id INTEGER NOT NULL CHECK(npc_id > 0),
            item_id INTEGER NOT NULL CHECK(item_id > 0),
            period TEXT NOT NULL,
            count INTEGER NOT NULL CHECK(count >= 0),
            PRIMARY KEY(owner_scope, owner_id, npc_id, item_id, period)
        );
        CREATE TABLE IF NOT EXISTS items ( item_uid INTEGER PRIMARY KEY AUTOINCREMENT CONSTRAINT world_identity_fits_the_wire CHECK (item_uid BETWEEN 1 AND 2147483647), item_core BLOB NOT NULL CHECK (length(item_core) = 82), cargo_character_id INTEGER, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, avatar_expire_date INTEGER, avatar_clear_id INTEGER, avatar_jewel_socket BLOB CHECK (avatar_jewel_socket IS NULL OR length(avatar_jewel_socket) = 30), avatar_color1 INTEGER, avatar_color2 INTEGER, avatar_delete_date INTEGER, creature_field04 INTEGER, creature_mode_flag INTEGER, creature_progress_value INTEGER, creature_mode1_field0a INTEGER, creature_mode1_field0b INTEGER, creature_field_after_value INTEGER, creature_text BLOB, creature_tail_flag INTEGER );
        CREATE TABLE IF NOT EXISTS item_slots ( owner_scope TEXT NOT NULL CHECK (owner_scope IN ('character', 'account')), owner_id INTEGER NOT NULL, list_type INTEGER NOT NULL, slot_index INTEGER NOT NULL, kind TEXT NOT NULL CHECK (kind IN ('world', 'template')), id INTEGER NOT NULL, core_uid INTEGER NOT NULL, count INTEGER NOT NULL, UNIQUE(owner_scope, owner_id, list_type, slot_index), CONSTRAINT slot_count_is_positive CHECK (count > 0), CONSTRAINT world_slot_holds_exactly_one CHECK (kind <> 'world' OR count = 1), CONSTRAINT world_slot_names_its_own_row CHECK (kind <> 'world' OR core_uid = id) );
        CREATE TABLE IF NOT EXISTS character_rentals ( character_id INTEGER NOT NULL, item_id INTEGER NOT NULL, inventory_template_id INTEGER NOT NULL DEFAULT 0, expire_time INTEGER NOT NULL, PRIMARY KEY (character_id, item_id) );
        CREATE TABLE IF NOT EXISTS character_item_cooldowns ( character_id INTEGER NOT NULL, sort_order INTEGER NOT NULL, item_id INTEGER NOT NULL, value INTEGER NOT NULL, PRIMARY KEY (character_id, sort_order) );
        CREATE TABLE IF NOT EXISTS character_item_effects ( character_id INTEGER NOT NULL, sort_order INTEGER NOT NULL, item_id INTEGER NOT NULL, value INTEGER NOT NULL, PRIMARY KEY (character_id, sort_order) );
        CREATE TABLE IF NOT EXISTS account_cargo_state ( account_id INTEGER PRIMARY KEY, selection_key INTEGER NOT NULL DEFAULT 0, value32 INTEGER NOT NULL DEFAULT 0, item_count INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP );
        CREATE TABLE IF NOT EXISTS account_increase_chance_lottery_progress(account_id INTEGER NOT NULL,item_template_id INTEGER NOT NULL,reward_index INTEGER NOT NULL,PRIMARY KEY(account_id,item_template_id,reward_index));
        CREATE TABLE IF NOT EXISTS account_premiums(account_id INTEGER NOT NULL,premium_type INTEGER NOT NULL,end_time INTEGER NOT NULL DEFAULT 0,updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,PRIMARY KEY(account_id,premium_type));
        CREATE TABLE IF NOT EXISTS account_purchase_restrictions(account_id INTEGER NOT NULL,restriction_id INTEGER NOT NULL CHECK(restriction_id>0),end_time INTEGER NOT NULL CHECK(end_time BETWEEN 1 AND 2147439600),PRIMARY KEY(account_id,restriction_id));
        CREATE TABLE IF NOT EXISTS account_settings(account_id INTEGER PRIMARY KEY,main_game_option BLOB,quickchat_bank0 BLOB,quickchat_bank1 BLOB,hotkey_key_type INTEGER NOT NULL DEFAULT 0,hotkey_slots BLOB);
        CREATE TABLE IF NOT EXISTS accounts ( account_id INTEGER PRIMARY KEY AUTOINCREMENT, m_id TEXT NOT NULL UNIQUE, password_hash TEXT NOT NULL DEFAULT '', last_login_ip TEXT NOT NULL DEFAULT '', last_login_at TEXT, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP , cera INTEGER NOT NULL DEFAULT 0, token_cera INTEGER NOT NULL DEFAULT 0, happy_token_cera INTEGER NOT NULL DEFAULT 0, lucky_star INTEGER NOT NULL DEFAULT 0, honor_exp INTEGER NOT NULL DEFAULT 0, growth_capsule_exp INTEGER NOT NULL DEFAULT 0, cube_black INTEGER NOT NULL DEFAULT 0, cube_white INTEGER NOT NULL DEFAULT 0, cube_red INTEGER NOT NULL DEFAULT 0, cube_blue INTEGER NOT NULL DEFAULT 0, cube_clear INTEGER NOT NULL DEFAULT 0, cube_gold INTEGER NOT NULL DEFAULT 0, soul_common INTEGER NOT NULL DEFAULT 0, soul_advanced INTEGER NOT NULL DEFAULT 0, soul_rare INTEGER NOT NULL DEFAULT 0, soul_unique INTEGER NOT NULL DEFAULT 0, soul_legendary INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS character_achievement_complete(character_id INTEGER, sort_order INTEGER, achievement_id INTEGER, p1 INTEGER, p2 INTEGER, p3 INTEGER, p4 INTEGER, PRIMARY KEY(character_id,achievement_id));
        CREATE TABLE IF NOT EXISTS character_active_quests(character_id INTEGER, slot INTEGER, quest_id INTEGER, trigger_value INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(character_id,slot));
        CREATE TABLE IF NOT EXISTS character_buy_restrict_items(character_id INTEGER NOT NULL,sort_order INTEGER NOT NULL,entry_id INTEGER NOT NULL,sentinel_or_value INTEGER NOT NULL,flag INTEGER NOT NULL,PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_collectbox_slots(character_id INTEGER, box_index INTEGER, slot_index INTEGER, item_id INTEGER, PRIMARY KEY(character_id,box_index,slot_index));
        CREATE TABLE IF NOT EXISTS character_condition ( action_byte INTEGER NOT NULL DEFAULT 0, aura_flag INTEGER NOT NULL DEFAULT 0, black_count INTEGER NOT NULL DEFAULT 0, champion_break_key_id INTEGER NOT NULL DEFAULT 0, champion_break_mode INTEGER NOT NULL DEFAULT 0, champion_break_value INTEGER NOT NULL DEFAULT 0, channel_display_mode INTEGER NOT NULL DEFAULT 0, channel_type INTEGER NOT NULL DEFAULT 0, chaos_point INTEGER NOT NULL DEFAULT 0, character_id INTEGER PRIMARY KEY, chat_ban_end_time INTEGER NOT NULL DEFAULT 0, costume_flag INTEGER NOT NULL DEFAULT 0, creature_buffer BLOB, creature_field1 INTEGER NOT NULL DEFAULT 0, creature_field2 INTEGER NOT NULL DEFAULT 0, creature_field3 INTEGER NOT NULL DEFAULT 0, creature_field4 INTEGER NOT NULL DEFAULT 0, crystal_cube_grade INTEGER NOT NULL DEFAULT 255, crystal_cube_type INTEGER NOT NULL DEFAULT 0, dimension_flag1 INTEGER NOT NULL DEFAULT 0, dimension_flag2 INTEGER NOT NULL DEFAULT 0, dimension_flag3 INTEGER NOT NULL DEFAULT 0, dimension_flag4 INTEGER NOT NULL DEFAULT 0, disguise_kind INTEGER NOT NULL DEFAULT 0, emotion_index INTEGER NOT NULL DEFAULT 0, fatigue_display_update INTEGER NOT NULL DEFAULT 0, fatigue_penalty INTEGER NOT NULL DEFAULT 0, fatigue_update INTEGER NOT NULL DEFAULT 0, hardcore_death_count INTEGER NOT NULL DEFAULT 0, is_disguised INTEGER NOT NULL DEFAULT 0, is_event_character INTEGER NOT NULL DEFAULT 0, is_hardcore_dead INTEGER NOT NULL DEFAULT 0, is_hardcore_mode INTEGER NOT NULL DEFAULT 0, is_premium_pc_room INTEGER NOT NULL DEFAULT 0, is_private_store INTEGER NOT NULL DEFAULT 0, is_return_user INTEGER NOT NULL DEFAULT 0, link_slot_enabled INTEGER NOT NULL DEFAULT 0, link_type_a INTEGER NOT NULL DEFAULT 0, link_type_b INTEGER NOT NULL DEFAULT 0, mood_value INTEGER NOT NULL DEFAULT 0, pc_room_id INTEGER NOT NULL DEFAULT 65537, pc_room_state INTEGER, pet_display_flag INTEGER NOT NULL DEFAULT 0, return_user_flag INTEGER NOT NULL DEFAULT 1, server_group_id INTEGER NOT NULL DEFAULT 0, stamina INTEGER NOT NULL DEFAULT 0, title_display_flag INTEGER NOT NULL DEFAULT 0, trailing_byte INTEGER NOT NULL DEFAULT 0 );
        CREATE TABLE IF NOT EXISTS character_container_state ( character_id INTEGER NOT NULL, list_type INTEGER NOT NULL, list_param16 INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (character_id, list_type) );
        CREATE TABLE IF NOT EXISTS character_daily_challenge_entries(character_id INTEGER, group_index INTEGER, entry_index INTEGER, track_like_id INTEGER, value_a INTEGER, value_b INTEGER, PRIMARY KEY(character_id,group_index,entry_index));
        CREATE TABLE IF NOT EXISTS character_daily_challenge_tail_ids(character_id INTEGER, sort_order INTEGER, id_value INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_daily_challenges ( character_id INTEGER, group_index INTEGER, group_id INTEGER, claimed_at TEXT, PRIMARY KEY(character_id,group_index) );
        CREATE TABLE IF NOT EXISTS character_fatigue(character_id INTEGER PRIMARY KEY, used INTEGER NOT NULL CHECK(used>=0), consumed_today INTEGER NOT NULL CHECK(consumed_today>=0), updated_at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS character_daily_counters(character_id INTEGER, counter_key TEXT, period TEXT, value INTEGER, last_consumed_at INTEGER, PRIMARY KEY(character_id,counter_key));
        CREATE TABLE IF NOT EXISTS character_daily_schedule_states(character_id INTEGER, sort_order INTEGER, param_a INTEGER, mode_or_state INTEGER, content_id INTEGER, param_b INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_dark_knight_combo_skill_pages(character_id INTEGER, page_index INTEGER, body BLOB NOT NULL, PRIMARY KEY(character_id,page_index));
        CREATE TABLE IF NOT EXISTS character_dimensions(character_id INTEGER, sort_order INTEGER, dim_key INTEGER, val1 INTEGER, val2 INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_dungeon_permissions(character_id INTEGER, sort_order INTEGER, dungeon_id INTEGER, clear_state INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_expert_job ( character_id INTEGER PRIMARY KEY, disjoint_machine_endurance INTEGER NOT NULL DEFAULT 0, disjoint_machine_grade INTEGER NOT NULL DEFAULT 0, enchanter_endurance INTEGER NOT NULL DEFAULT 0, expert_job_exp INTEGER NOT NULL DEFAULT 0, expert_job_type INTEGER NOT NULL DEFAULT 0, giveup_count INTEGER NOT NULL DEFAULT 0 );
        CREATE TABLE IF NOT EXISTS character_expert_job_recipes(character_id INTEGER, recipe_id INTEGER, PRIMARY KEY(character_id,recipe_id));
        CREATE TABLE IF NOT EXISTS character_growth_weapon_stages(character_id INTEGER, sort_order INTEGER, stage_id INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_hotkey_slots(character_id INTEGER, slot_index INTEGER, hotkey_value INTEGER, PRIMARY KEY(character_id,slot_index));
        CREATE TABLE IF NOT EXISTS character_invisible_flags ( character_id INTEGER, slot_index INTEGER, flag_value INTEGER, PRIMARY KEY(character_id,slot_index) );
        CREATE TABLE IF NOT EXISTS character_item_locks ( character_id INTEGER NOT NULL, equipment_lock_id INTEGER NOT NULL, inventory_list_type INTEGER NOT NULL, slot INTEGER NOT NULL, state INTEGER NOT NULL, remaining_seconds INTEGER, PRIMARY KEY (character_id, equipment_lock_id) );
        CREATE TABLE IF NOT EXISTS character_knight_shield_deck(character_id INTEGER, slot_index INTEGER, shield_item_id INTEGER, PRIMARY KEY(character_id,slot_index));
        CREATE TABLE IF NOT EXISTS character_mercenary_support(owner_character_id INTEGER NOT NULL,slot INTEGER NOT NULL,support_character_id INTEGER NOT NULL,skill_id INTEGER NOT NULL,striker_skill_id INTEGER NOT NULL DEFAULT 0,updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,PRIMARY KEY(owner_character_id,slot));
        CREATE TABLE IF NOT EXISTS character_pvp ( character_id INTEGER PRIMARY KEY, pvp_grade INTEGER NOT NULL DEFAULT 0, pvp_lose_streak INTEGER NOT NULL DEFAULT 0, pvp_rank_point INTEGER NOT NULL DEFAULT 0, pvp_rating_grade INTEGER NOT NULL DEFAULT 0, pvp_stat_a INTEGER NOT NULL DEFAULT 0, pvp_win_streak INTEGER NOT NULL DEFAULT 0, skills_initialized INTEGER NOT NULL DEFAULT 0, FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE );
        CREATE TABLE IF NOT EXISTS character_pvp_missions(character_id INTEGER, sort_order INTEGER, mission_id INTEGER, progress_value INTEGER, PRIMARY KEY(character_id,sort_order));
        CREATE TABLE IF NOT EXISTS character_skill_builds(character_id INTEGER NOT NULL,build_id INTEGER NOT NULL CHECK(build_id >= 0),unlocked INTEGER NOT NULL CHECK(unlocked IN (0,1)),PRIMARY KEY(character_id,build_id),FOREIGN KEY(character_id) REFERENCES characters(character_id) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS character_skills ( character_id INTEGER NOT NULL, mode TEXT NOT NULL CHECK (mode IN ('pve', 'pvp')), page_index INTEGER NOT NULL, slot INTEGER NOT NULL, skill_id INTEGER NOT NULL, level INTEGER NOT NULL, extra_values BLOB, PRIMARY KEY (character_id, mode, page_index, slot), UNIQUE (character_id, mode, page_index, skill_id), CHECK (mode <> 'pvp' OR page_index BETWEEN 0 AND 1), FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE );
        CREATE TABLE IF NOT EXISTS character_quest_notify_selections(character_id INTEGER, slot_index INTEGER, quest_id INTEGER, PRIMARY KEY(character_id,slot_index));
        CREATE TABLE IF NOT EXISTS character_settings ( character_id INTEGER PRIMARY KEY, character_option_blob BLOB, user_state_bits INTEGER NOT NULL DEFAULT 3 );
        CREATE TABLE IF NOT EXISTS character_stats ( auction_gold_limit INTEGER NOT NULL DEFAULT 0, carried_gold INTEGER NOT NULL DEFAULT 0, character_id INTEGER PRIMARY KEY, current_hp_mp_percent INTEGER NOT NULL DEFAULT 0, equip_list_trailing INTEGER NOT NULL DEFAULT 0, equipped_creature_level INTEGER NOT NULL DEFAULT 0, flag_byte INTEGER NOT NULL DEFAULT 0, gold_carry_limit INTEGER NOT NULL DEFAULT 0, progress1 INTEGER NOT NULL DEFAULT 0, progress2 INTEGER NOT NULL DEFAULT 0, quest_shop_count INTEGER NOT NULL DEFAULT 0, server_timestamp INTEGER NOT NULL DEFAULT 0, stat_attack_speed INTEGER NOT NULL DEFAULT 0, stat_cast_speed INTEGER NOT NULL DEFAULT 0, stat_dark_resistance INTEGER NOT NULL DEFAULT 0, stat_fire_resistance INTEGER NOT NULL DEFAULT 0, stat_hit_recovery INTEGER NOT NULL DEFAULT 0, stat_hp_max INTEGER NOT NULL DEFAULT 0, stat_hp_regen_speed INTEGER NOT NULL DEFAULT 0, stat_inventory_limit INTEGER NOT NULL DEFAULT 0, stat_jump_power INTEGER NOT NULL DEFAULT 0, stat_light_resistance INTEGER NOT NULL DEFAULT 0, stat_magical_attack INTEGER NOT NULL DEFAULT 0, stat_magical_defense INTEGER NOT NULL DEFAULT 0, stat_move_speed INTEGER NOT NULL DEFAULT 0, stat_mp_max INTEGER NOT NULL DEFAULT 0, stat_mp_regen_speed INTEGER NOT NULL DEFAULT 0, stat_physical_attack INTEGER NOT NULL DEFAULT 0, stat_physical_defense INTEGER NOT NULL DEFAULT 0, stat_water_resistance INTEGER NOT NULL DEFAULT 0, stat_weight INTEGER NOT NULL DEFAULT 0 );
        CREATE TABLE IF NOT EXISTS characters ( account_id INTEGER NOT NULL, appearance_blob BLOB, area_id INTEGER NOT NULL DEFAULT 0, area_state INTEGER NOT NULL DEFAULT 3, bonus_sp INTEGER NOT NULL DEFAULT 0, bonus_tp INTEGER NOT NULL DEFAULT 0, character_id INTEGER PRIMARY KEY, clone_title_item_id INTEGER NOT NULL DEFAULT 0, created_at INTEGER NOT NULL DEFAULT 0, delete_flag INTEGER NOT NULL DEFAULT 0, direction INTEGER NOT NULL DEFAULT 5, ex_equip_slot_stat INTEGER NOT NULL DEFAULT 0, exp INTEGER NOT NULL DEFAULT 0, grow_type INTEGER NOT NULL DEFAULT 0, guild_level INTEGER NOT NULL DEFAULT 0, guild_power_war INTEGER NOT NULL DEFAULT 0, job INTEGER NOT NULL DEFAULT 0, level INTEGER NOT NULL DEFAULT 1, manage_level INTEGER NOT NULL DEFAULT 0, name TEXT NOT NULL, name_bytes BLOB, name_tag_expire_time INTEGER NOT NULL DEFAULT 0, name_tag_item_id INTEGER NOT NULL DEFAULT 0, pos_x INTEGER NOT NULL DEFAULT 0, pos_y INTEGER NOT NULL DEFAULT 0, skill_tree_index INTEGER NOT NULL DEFAULT -1, slot_index INTEGER NOT NULL DEFAULT 0, town_id INTEGER NOT NULL DEFAULT 0, user_state INTEGER NOT NULL DEFAULT 0 );
        CREATE TABLE IF NOT EXISTS dungeon_persistent_effect_outbox( source_event_id TEXT NOT NULL, effect_kind TEXT NOT NULL, effect_scope INTEGER NOT NULL, scope_target INTEGER NOT NULL, character_id INTEGER NOT NULL, account_id INTEGER NOT NULL, payload_version INTEGER NOT NULL DEFAULT 1, payload_json TEXT NOT NULL DEFAULT '{}', state INTEGER NOT NULL DEFAULT 0, lease_id TEXT, lease_owner TEXT, lease_expires_at INTEGER NOT NULL DEFAULT 0, attempt_count INTEGER NOT NULL DEFAULT 0, last_error TEXT NOT NULL DEFAULT '', result_version INTEGER, result_json TEXT, created_at INTEGER NOT NULL DEFAULT 0, updated_at INTEGER NOT NULL DEFAULT 0, committed_at INTEGER, PRIMARY KEY(source_event_id,effect_kind,effect_scope,scope_target));
        CREATE TABLE IF NOT EXISTS mailbox_attachments( attachment_id INTEGER PRIMARY KEY AUTOINCREMENT, message_id INTEGER NOT NULL, ordinal INTEGER NOT NULL DEFAULT 0, item_type INTEGER NOT NULL DEFAULT 0, source_list_type INTEGER NOT NULL DEFAULT 0, source_slot_index INTEGER NOT NULL DEFAULT 0, source_item_uid INTEGER NOT NULL DEFAULT 0, item_template_id INTEGER NOT NULL, item_kind TEXT NOT NULL DEFAULT 'unknown', item_count INTEGER NOT NULL DEFAULT 1, instance_value INTEGER NOT NULL DEFAULT 0, durability INTEGER NOT NULL DEFAULT 0, seal_flag INTEGER NOT NULL DEFAULT 0, option_value INTEGER NOT NULL DEFAULT 0, expire_time INTEGER NOT NULL DEFAULT 0, marker_16 INTEGER NOT NULL DEFAULT -1, pet_serial_or_handle INTEGER NOT NULL DEFAULT 0, item_core BLOB, detail_json TEXT NOT NULL DEFAULT '');
        CREATE TABLE IF NOT EXISTS mailbox_messages( message_id INTEGER PRIMARY KEY AUTOINCREMENT, sender_character_id INTEGER NOT NULL DEFAULT 0, sender_name TEXT NOT NULL DEFAULT '', receiver_character_id INTEGER NOT NULL, title TEXT NOT NULL DEFAULT '', body TEXT NOT NULL DEFAULT '', gold INTEGER NOT NULL DEFAULT 0, mail_type INTEGER NOT NULL DEFAULT 0, source_protocol INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, unlimited_flag INTEGER NOT NULL DEFAULT 0, expire_at TEXT NOT NULL DEFAULT '9999-12-31 23:59:59', deleted_by_sender INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS mailbox_recipients( recipient_id INTEGER PRIMARY KEY AUTOINCREMENT, message_id INTEGER NOT NULL, character_id INTEGER NOT NULL, folder INTEGER NOT NULL DEFAULT 0, read_flag INTEGER NOT NULL DEFAULT 0, saved_flag INTEGER NOT NULL DEFAULT 0, deleted_flag INTEGER NOT NULL DEFAULT 0, received_gold_flag INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, UNIQUE(message_id,character_id,folder));
        """;

    private const string IndexDeclarations = """
        CREATE INDEX IF NOT EXISTS ix_gm_audit_time ON gm_audit(occurred_at DESC,operation_id DESC);
        CREATE INDEX IF NOT EXISTS ix_gm_mail_operations_time ON gm_mail_operations(administrator_id,committed_at DESC,request_id DESC);
        CREATE INDEX IF NOT EXISTS ix_gm_sessions_expiry ON gm_sessions(expires_at);
        CREATE INDEX IF NOT EXISTS idx_item_slots_core ON item_slots(core_uid);
        CREATE UNIQUE INDEX IF NOT EXISTS idx_item_slots_account_slot ON item_slots(owner_id, slot_index) WHERE owner_scope = 'account';
        CREATE INDEX IF NOT EXISTS idx_characters_account ON characters(account_id, delete_flag, slot_index);
        CREATE UNIQUE INDEX IF NOT EXISTS idx_mailbox_pending_equipment_uid ON mailbox_attachments(source_item_uid) WHERE asset_kind='equipment' AND claimed_flag=0 AND source_item_uid>0;
        """;

    /// <summary>Table, column and declaration, one per line.</summary>
    private const string ReconcilableColumns = """
        character_stats|carried_gold|INTEGER NOT NULL DEFAULT 0
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
        accounts|seria_luck_value|INTEGER NOT NULL DEFAULT 0
        accounts|honor_exp|INTEGER NOT NULL DEFAULT 0
        accounts|growth_capsule_exp|INTEGER NOT NULL DEFAULT 0
        accounts|cube_black|INTEGER NOT NULL DEFAULT 0
        accounts|cube_white|INTEGER NOT NULL DEFAULT 0
        accounts|cube_red|INTEGER NOT NULL DEFAULT 0
        accounts|cube_blue|INTEGER NOT NULL DEFAULT 0
        accounts|cube_clear|INTEGER NOT NULL DEFAULT 0
        accounts|cube_gold|INTEGER NOT NULL DEFAULT 0
        accounts|soul_common|INTEGER NOT NULL DEFAULT 0
        accounts|soul_advanced|INTEGER NOT NULL DEFAULT 0
        accounts|soul_rare|INTEGER NOT NULL DEFAULT 0
        accounts|soul_unique|INTEGER NOT NULL DEFAULT 0
        accounts|soul_legendary|INTEGER NOT NULL DEFAULT 0
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
        character_collectbox_slots|character_id|INTEGER
        character_collectbox_slots|box_index|INTEGER
        character_collectbox_slots|slot_index|INTEGER
        character_collectbox_slots|item_id|INTEGER
        character_container_state|list_param16|INTEGER NOT NULL DEFAULT 0
        character_daily_challenge_entries|character_id|INTEGER
        character_daily_challenge_entries|group_index|INTEGER
        character_daily_challenge_entries|entry_index|INTEGER
        character_daily_challenge_entries|track_like_id|INTEGER
        character_daily_challenge_entries|value_a|INTEGER
        character_daily_challenge_entries|value_b|INTEGER
        character_daily_challenge_tail_ids|character_id|INTEGER
        character_daily_challenge_tail_ids|sort_order|INTEGER
        character_daily_challenge_tail_ids|id_value|INTEGER
        character_daily_counters|character_id|INTEGER
        character_daily_counters|counter_key|TEXT
        character_daily_counters|period|TEXT
        character_daily_counters|value|INTEGER
        character_daily_counters|last_consumed_at|INTEGER
        character_daily_schedule_states|character_id|INTEGER
        character_daily_schedule_states|sort_order|INTEGER
        character_daily_schedule_states|param_a|INTEGER
        character_daily_schedule_states|mode_or_state|INTEGER
        character_daily_schedule_states|content_id|INTEGER
        character_daily_schedule_states|param_b|INTEGER
        character_dark_knight_combo_skill_pages|character_id|INTEGER
        character_dark_knight_combo_skill_pages|page_index|INTEGER
        character_dimensions|character_id|INTEGER
        character_dimensions|sort_order|INTEGER
        character_dimensions|dim_key|INTEGER
        character_dimensions|val1|INTEGER
        character_dimensions|val2|INTEGER
        character_dungeon_permissions|character_id|INTEGER
        character_dungeon_permissions|sort_order|INTEGER
        character_dungeon_permissions|dungeon_id|INTEGER
        character_dungeon_permissions|clear_state|INTEGER
        character_expert_job_recipes|character_id|INTEGER
        character_expert_job_recipes|recipe_id|INTEGER
        character_growth_weapon_stages|character_id|INTEGER
        character_growth_weapon_stages|sort_order|INTEGER
        character_growth_weapon_stages|stage_id|INTEGER
        character_hotkey_slots|character_id|INTEGER
        character_hotkey_slots|slot_index|INTEGER
        character_hotkey_slots|hotkey_value|INTEGER
        character_item_locks|remaining_seconds|INTEGER
        character_knight_shield_deck|character_id|INTEGER
        character_knight_shield_deck|slot_index|INTEGER
        character_knight_shield_deck|shield_item_id|INTEGER
        character_mercenary_support|striker_skill_id|INTEGER NOT NULL DEFAULT 0
        character_pvp_missions|character_id|INTEGER
        character_pvp_missions|sort_order|INTEGER
        character_pvp_missions|mission_id|INTEGER
        character_pvp_missions|progress_value|INTEGER
        character_quest_notify_selections|character_id|INTEGER
        character_quest_notify_selections|slot_index|INTEGER
        character_quest_notify_selections|quest_id|INTEGER
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
        mailbox_attachments|asset_kind|TEXT NOT NULL DEFAULT 'legacy'
        mailbox_attachments|claimed_flag|INTEGER NOT NULL DEFAULT 0
        mailbox_attachments|gold_amount|INTEGER NOT NULL DEFAULT 0
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
        characters|appearance_blob|BLOB
        characters|area_id|INTEGER NOT NULL DEFAULT 0
        characters|area_state|INTEGER NOT NULL DEFAULT 3
        characters|bonus_sp|INTEGER NOT NULL DEFAULT 0
        characters|bonus_tp|INTEGER NOT NULL DEFAULT 0
        characters|clone_title_item_id|INTEGER NOT NULL DEFAULT 0
        characters|created_at|INTEGER NOT NULL DEFAULT 0
        characters|delete_flag|INTEGER NOT NULL DEFAULT 0
        characters|direction|INTEGER NOT NULL DEFAULT 5
        characters|ex_equip_slot_stat|INTEGER NOT NULL DEFAULT 0
        characters|exp|INTEGER NOT NULL DEFAULT 0
        characters|grow_type|INTEGER NOT NULL DEFAULT 0
        characters|guild_level|INTEGER NOT NULL DEFAULT 0
        characters|guild_power_war|INTEGER NOT NULL DEFAULT 0
        characters|job|INTEGER NOT NULL DEFAULT 0
        characters|level|INTEGER NOT NULL DEFAULT 1
        characters|manage_level|INTEGER NOT NULL DEFAULT 0
        characters|name_bytes|BLOB
        characters|name_tag_expire_time|INTEGER NOT NULL DEFAULT 0
        characters|name_tag_item_id|INTEGER NOT NULL DEFAULT 0
        characters|pos_x|INTEGER NOT NULL DEFAULT 0
        characters|pos_y|INTEGER NOT NULL DEFAULT 0
        characters|active_skill_build|INTEGER
        characters|skill_tree_index|INTEGER NOT NULL DEFAULT -1
        characters|slot_index|INTEGER NOT NULL DEFAULT 0
        characters|town_id|INTEGER NOT NULL DEFAULT 0
        characters|user_state|INTEGER NOT NULL DEFAULT 0
        character_expert_job|disjoint_machine_endurance|INTEGER NOT NULL DEFAULT 0
        character_expert_job|disjoint_machine_grade|INTEGER NOT NULL DEFAULT 0
        character_expert_job|enchanter_endurance|INTEGER NOT NULL DEFAULT 0
        character_expert_job|expert_job_exp|INTEGER NOT NULL DEFAULT 0
        character_expert_job|expert_job_type|INTEGER NOT NULL DEFAULT 0
        character_expert_job|giveup_count|INTEGER NOT NULL DEFAULT 0
        """;
}
