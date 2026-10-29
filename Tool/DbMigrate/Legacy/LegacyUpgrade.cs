using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Legacy;

/// <summary>
/// Brings a database from before schema versions to version 1.
/// </summary>
/// <remarks>
/// Until version 1 the server put every database into shape each time it opened one: it created
/// the tables it lacked, added the columns an older table lacked, and ran each data migration
/// that had not run yet. Those databases carry no version, so this repeats exactly that, in the
/// order the server did it, and then removes what version 1 no longer has. Every step checks what
/// it finds rather than assuming one particular older shape, because a database could have last
/// been opened by any of the servers before.
/// </remarks>
internal static class LegacyUpgrade
{
    /// <summary>Columns an older table may lack: table, column and declaration, one per line.</summary>
    private const string MissingColumns = """
        marriage_weddings|completed_at|INTEGER NOT NULL DEFAULT 0
        items|is_mirror|INTEGER NOT NULL DEFAULT 0 CHECK(is_mirror IN (0,1))
        items|mirror_source_uid|INTEGER
        items|appearance_item_id|INTEGER CHECK(appearance_item_id >= 0)
        character_stats|carried_gold|INTEGER NOT NULL DEFAULT 0
        account_cargo_state|selection_key|INTEGER NOT NULL DEFAULT 0
        account_cargo_state|value32|INTEGER NOT NULL DEFAULT 0
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
        mailbox_messages|mail_type|INTEGER NOT NULL DEFAULT 0
        mailbox_messages|source_protocol|INTEGER NOT NULL DEFAULT 0
        mailbox_messages|delivery_key|TEXT NOT NULL DEFAULT ''
        mailbox_messages|unlimited_flag|INTEGER NOT NULL DEFAULT 0
        mailbox_messages|expire_at|TEXT NOT NULL DEFAULT '9999-12-31 23:59:59'
        mailbox_messages|deleted_by_sender|INTEGER NOT NULL DEFAULT 0
        mailbox_recipients|folder|INTEGER NOT NULL DEFAULT 0
        mailbox_recipients|read_flag|INTEGER NOT NULL DEFAULT 0
        mailbox_recipients|saved_flag|INTEGER NOT NULL DEFAULT 0
        mailbox_recipients|deleted_flag|INTEGER NOT NULL DEFAULT 0
        characters|area_id|INTEGER NOT NULL DEFAULT 0
        characters|area_state|INTEGER NOT NULL DEFAULT 3
        characters|bonus_sp|INTEGER NOT NULL DEFAULT 0
        characters|bonus_tp|INTEGER NOT NULL DEFAULT 0
        characters|clone_title_item_id|INTEGER NOT NULL DEFAULT 0
        characters|tutorial_completed|INTEGER NOT NULL DEFAULT 0
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
        characters|slot_index|INTEGER NOT NULL DEFAULT 0
        characters|town_id|INTEGER NOT NULL DEFAULT 0
        characters|last_inn_town_id|INTEGER NOT NULL DEFAULT 0
        characters|user_state|INTEGER NOT NULL DEFAULT 0
        character_expert_job|disjoint_machine_endurance|INTEGER NOT NULL DEFAULT 0
        character_expert_job|disjoint_machine_grade|INTEGER NOT NULL DEFAULT 0
        character_expert_job|enchanter_endurance|INTEGER NOT NULL DEFAULT 0
        character_expert_job|expert_job_exp|INTEGER NOT NULL DEFAULT 0
        character_expert_job|expert_job_type|INTEGER NOT NULL DEFAULT 0
        character_expert_job|giveup_count|INTEGER NOT NULL DEFAULT 0
        """;

    public static void Apply(SqliteConnection connection)
    {
        Execute(connection, """
            DROP TABLE IF EXISTS login_tickets;
            CREATE TABLE IF NOT EXISTS schema_migrations(name TEXT PRIMARY KEY, applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
            CREATE TABLE IF NOT EXISTS marriage_housing_migrations(version INTEGER PRIMARY KEY, applied_at INTEGER NOT NULL);
            """);
        CharacterDatabaseSchema.DeclareTables(connection, null);
        AddMissingColumns(connection);
        AvatarAppearanceMigration.Apply(connection);
        MailboxGoldMigration(connection);
        MarriageRelationMigration.Apply(connection);
        MarriageHousingMigration.Apply(connection);
        Execute(connection, "DROP INDEX IF EXISTS idx_item_slots_account_slot;");
        CharacterDatabaseSchema.DeclareIndexes(connection, null);
        SharedMaterialMigration.Apply(connection);
        JoustRoundIdentityMigration.Apply(connection);
        RetiredColumnMigration.Apply(connection);
        // What recorded which of the steps above had run; the version now says it for all of them.
        Execute(connection, """
            DROP TABLE schema_migrations;
            DROP TABLE marriage_housing_migrations;
            """);
    }

    private static void AddMissingColumns(SqliteConnection connection)
    {
        var columns = MissingColumns
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|'));
        foreach (var column in columns)
        {
            if (!Shape.HasTable(connection, null, column[0])
                || Shape.HasColumn(connection, null, column[0], column[1]))
                continue;
            Execute(connection, $"ALTER TABLE {column[0]} ADD COLUMN {column[1]} {column[2]};");
        }
    }

    // Message-level gold predates gold attachments. A database that still has the column gets
    // each such amount as one attachment, claimed if the old flag said it was received, before
    // RetiredColumnMigration drops both columns.
    private static void MailboxGoldMigration(SqliteConnection connection)
    {
        if (!Shape.HasColumn(connection, null, "mailbox_messages", "gold")) return;
        var claimed = Shape.HasColumn(connection, null, "mailbox_recipients", "received_gold_flag")
            ? """
              CASE WHEN EXISTS (SELECT 1 FROM mailbox_recipients r
                  WHERE r.message_id=m.message_id AND r.character_id=m.receiver_character_id
                    AND r.received_gold_flag<>0) THEN 1 ELSE 0 END
              """
            : "0";
        Execute(connection, $"""
            INSERT INTO mailbox_attachments
                (message_id, ordinal, item_template_id, asset_kind, gold_amount, claimed_flag)
            SELECT m.message_id, COALESCE((SELECT MAX(a.ordinal) + 1 FROM mailbox_attachments a
                    WHERE a.message_id = m.message_id), 0), 0, 'gold', m.gold,
                   {claimed}
            FROM mailbox_messages m
            WHERE m.gold > 0 AND NOT EXISTS
                (SELECT 1 FROM mailbox_attachments a
                 WHERE a.message_id=m.message_id AND a.asset_kind='gold');
            """);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
