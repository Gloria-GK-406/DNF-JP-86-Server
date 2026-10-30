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
            SELECT account_id, job, level, clone_title_item_id,
                   exp, ex_equip_slot_stat, user_state, name_tag_item_id,
                   name_tag_expire_time, manage_level, guild_power_war, guild_level
            FROM characters
            WHERE character_id=@cid AND delete_flag=0;
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterEntryRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt64(3),
                reader.GetInt64(4), reader.GetInt32(5), reader.GetInt32(6),
                reader.GetInt64(7), reader.GetInt64(8), reader.GetInt32(9), reader.GetInt64(10),
                reader.GetInt32(11)),
            cancellationToken);

    public ValueTask<BonusPointRow?> FindBonusPointsAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            "SELECT bonus_sp, bonus_tp FROM characters WHERE character_id=@cid",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new BonusPointRow(reader.GetInt32(0), reader.GetInt32(1)),
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

/// <summary>The <c>character_invisible_flags</c> table.</summary>
/// <remarks>The name is the original server's spelling, kept so the column can be found.</remarks>
internal sealed class SqliteCharacterQuestClearFlagTable(SqliteDatabase database)
    : ICharacterQuestClearFlagTable
{
    public ValueTask<IReadOnlyList<QuestClearFlagRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT slot_index, flag_value FROM character_invisible_flags "
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

/// <summary>Account difficulty permissions reached through a character's ownership.</summary>
internal sealed class SqliteCharacterDungeonPermissionTable(SqliteDatabase database)
    : ICharacterDungeonPermissionTable
{
    public ValueTask<IReadOnlyList<DungeonPermissionRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            WITH owner AS (SELECT account_id FROM characters WHERE character_id=@cid),
            permissions AS (
                SELECT dungeon_id,clear_state FROM account_dungeon_permissions
                WHERE account_id=(SELECT account_id FROM owner)
                UNION ALL
                SELECT p.dungeon_id,p.clear_state FROM character_dungeon_permissions p
                JOIN characters c ON c.character_id=p.character_id
                WHERE c.account_id=(SELECT account_id FROM owner)
            )
            SELECT 0,dungeon_id,clear_state FROM permissions ORDER BY dungeon_id
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new DungeonPermissionRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
            cancellationToken);

    public ValueTask RaiseMaximumAsync(CharacterId characterId, int dungeonId, byte maximum, CancellationToken cancellationToken) =>
        database.WriteAsync(async (command, token) =>
        {
            // Legacy character rows remain read-only. New grants have one account/dungeon owner.
            command.CommandText = """
                INSERT INTO account_dungeon_permissions(account_id,dungeon_id,clear_state)
                SELECT account_id,@did,@maximum FROM characters WHERE character_id=@cid
                ON CONFLICT(account_id,dungeon_id) DO UPDATE SET
                    clear_state=MAX(account_dungeon_permissions.clear_state,excluded.clear_state);
                """;
            command.Parameters.AddWithValue("@cid", characterId.Value);
            command.Parameters.AddWithValue("@did", dungeonId);
            command.Parameters.AddWithValue("@maximum", maximum);
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                throw new InvalidOperationException("Dungeon permission character has no account owner.");
        }, cancellationToken);

}

/// <summary>The creature rows of <c>items</c>: a character's creatures, each one a world row.</summary>
/// <remarks>
/// The eight facts used to be a row of <c>character_creatures</c> kept under a key of its own, and
/// they are columns of the world row now. The creature and its condition are one row, so the state
/// follows the creature rather than the position it is standing in, and there is no second row
/// left to disagree with the first. Which rows are creatures is decided by the first of the eight,
/// exactly as an avatar's presence is decided by its UID.
///
/// <b>The identity is the key.</b> <c>creature_key</c> is gone, and <c>item_uid</c> is what the
/// creature list frame writes as each entry's first field. There is one identity space and it is
/// the narrow one the wire can say, which the table itself holds the allocation to.
///
/// <b>The order is the wire order.</b> The frame writes its entries in the order this read answers
/// in. That order was ascending <c>character_creatures.sort_order</c> -- a per-character sequence
/// the primary key admitted one row apiece of -- and it is ascending <c>item_uid</c> now. The two
/// agree in what matters about them: each is a total order over exactly this character's
/// creatures, each runs in the order the creatures were acquired, and neither moves when a
/// creature does -- nothing ever rewrote the old column on a move, and the identity ordered on
/// here is the one thing a move is defined not to change. Ordering on the position instead would
/// have reshuffled the list every time a creature was picked up and put down.
///
/// The rank is answered rather than stored, because the column that carried it is gone and the
/// caller sorts on it. Dense from zero, exactly as that column was.
///
/// <b>Whose creature it is comes from the position holding it.</b> A world row says what a thing
/// is and no longer says whose it is, so this asks <c>item_slots</c> -- which is also what makes a
/// creature nobody holds absent from every character's list rather than present in one of them.
/// </remarks>
internal sealed class SqliteCharacterCreatureTable(SqliteDatabase database)
    : ICharacterCreatureTable
{
    public async ValueTask<IReadOnlyList<CharacterPetStorageRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var rows = await database.ReadAllAsync(
            """
            SELECT held.item_uid, held.creature_field04, held.creature_mode_flag,
                   held.creature_progress_value, held.creature_mode1_field0a,
                   held.creature_mode1_field0b, held.creature_field_after_value,
                   held.creature_text, held.creature_tail_flag
            FROM items held
            WHERE held.creature_field04 IS NOT NULL
              AND EXISTS (
                  SELECT 1 FROM item_slots place
                  WHERE place.owner_scope = 'character' AND place.owner_id = @cid
                    AND place.core_uid = held.item_uid)
            ORDER BY held.item_uid;
            """,
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => new CharacterPetStorageRow(
                0, reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
                EntryBlob.Bounded(reader, 7, EntryBlob.CreatureText, "creature text", true),
                reader.GetInt32(8)),
            cancellationToken).ConfigureAwait(false);
        return [.. rows.Select((row, rank) => row with { SortOrder = rank })];
    }
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
