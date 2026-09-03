using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Schema;

/// <summary>
/// The character's own tables, against the shape the ruling settled on.
/// </summary>
/// <remarks>
/// The ruling in <c>Docs/Refactor/TableOwnershipRuling.md</c> decides where every column of the
/// character body goes. What follows is that decision written as assertions, and it exists because
/// nothing else would notice if the move dropped something: a missing column reads as a default, a
/// missing constraint refuses nothing, and neither produces a compiler error or a red test.
///
/// The column lists are the ruling's own, taken from its per-column ledger rather than retyped from
/// its prose -- the ledger is the part of it that can be checked mechanically.
/// </remarks>
public sealed class CharacterBodyShapeTests
{
    /// <summary>characters -- 28 columns.</summary>
    private static readonly string[] Characters =
    [
        "account_id", "appearance_blob", "area_id", "area_state", "bonus_sp", "bonus_tp",
        "character_id", "clone_title_item_id", "created_at", "delete_flag", "direction",
        "ex_equip_slot_stat", "exp", "grow_type", "guild_level", "guild_power_war", "job",
        "level", "manage_level", "name", "name_bytes", "name_tag_expire_time",
        "name_tag_item_id", "pos_x", "pos_y", "skill_tree_index", "slot_index", "town_id",
        "user_state",
    ];

    /// <summary>character_stats -- 31 columns.</summary>
    /// <remarks>
    /// <c>carried_gold</c> is the gold on the player's bag. It lives beside the cap that governs
    /// it rather than in the item rows, because a gold change has to be one statement -- see
    /// <c>ICharacterCarriedGoldTable</c>.
    /// </remarks>
    private static readonly string[] CharacterStats =
    [
        "auction_gold_limit", "carried_gold", "character_id", "current_hp_mp_percent",
        "equip_list_trailing",
        "equipped_creature_level", "flag_byte", "gold_carry_limit", "progress1", "progress2",
        "quest_shop_count", "server_timestamp", "stat_attack_speed", "stat_cast_speed",
        "stat_dark_resistance", "stat_fire_resistance", "stat_hit_recovery", "stat_hp_max",
        "stat_hp_regen_speed", "stat_inventory_limit", "stat_jump_power",
        "stat_light_resistance", "stat_magical_attack", "stat_magical_defense",
        "stat_move_speed", "stat_mp_max", "stat_mp_regen_speed", "stat_physical_attack",
        "stat_physical_defense", "stat_water_resistance", "stat_weight",
    ];

    /// <summary>character_condition -- 48 columns.</summary>
    private static readonly string[] CharacterCondition =
    [
        "action_byte", "aura_flag", "black_count", "champion_break_key_id",
        "champion_break_mode", "champion_break_value", "channel_display_mode", "channel_type",
        "chaos_point", "character_id", "chat_ban_end_time", "costume_flag", "creature_buffer",
        "creature_field1", "creature_field2", "creature_field3", "creature_field4",
        "crystal_cube_grade", "crystal_cube_type", "dimension_flag1", "dimension_flag2",
        "dimension_flag3", "dimension_flag4", "disguise_kind", "emotion_index",
        "fatigue_display_update", "fatigue_penalty", "fatigue_update", "hardcore_death_count",
        "is_disguised", "is_event_character", "is_hardcore_dead", "is_hardcore_mode",
        "is_premium_pc_room", "is_private_store", "is_return_user", "link_slot_enabled",
        "link_type_a", "link_type_b", "mood_value", "pc_room_id", "pc_room_state",
        "pet_display_flag", "return_user_flag", "server_group_id", "stamina",
        "title_display_flag", "trailing_byte",
    ];

    /// <summary>character_pvp -- 8 columns.</summary>
    private static readonly string[] CharacterPvp =
    [
        "character_id", "pvp_grade", "pvp_lose_streak", "pvp_rank_point", "pvp_rating_grade",
        "pvp_stat_a", "pvp_win_streak", "skills_initialized",
    ];

    /// <summary>character_settings -- 3 columns.</summary>
    private static readonly string[] CharacterSettings =
    [
        "character_id", "character_option_blob", "user_state_bits",
    ];

    /// <summary>character_expert_job -- 7 columns.</summary>
    private static readonly string[] CharacterExpertJob =
    [
        "character_id", "disjoint_machine_endurance", "disjoint_machine_grade",
        "enchanter_endurance", "expert_job_exp", "expert_job_type", "giveup_count",
    ];

    /// <summary>The five tables whose columns move into the six above.</summary>
    /// <remarks>
    /// Each holds at most one row per character, and each one's columns belong to a concept that
    /// already has a home. <c>character_expert_job</c> is a sixth table of that same shape and it
    /// stays, because what it holds is a concept of its own -- which is why it is asserted present
    /// here beside the five that go.
    /// </remarks>
    private static readonly string[] FoldedAway =
    [
        "character_init_flags", "character_gold_limits", "character_crystal_contract",
        "character_dimension_flags", "character_pvp_skill_state",
    ];

    [Theory]
    [MemberData(nameof(Bodies))]
    public async Task ACharacterBodyTableHasExactlyTheColumnsTheRulingGivesIt(
        string table, string[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var shape = await ShapeAsync();

        Assert.True(shape.ContainsKey(table), $"{table} is not in the database at all.");
        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), shape[table]);
    }

    public static TheoryData<string, string[]> Bodies() => new()
    {
        { "characters", Characters },
        { "character_stats", CharacterStats },
        { "character_condition", CharacterCondition },
        { "character_pvp", CharacterPvp },
        { "character_settings", CharacterSettings },
        { "character_expert_job", CharacterExpertJob },
    };

    /// <remarks>
    /// A table that can only ever hold one row per owner is a column group wearing a table's
    /// clothes. These five are gone because their columns belong elsewhere, not because they were
    /// narrow.
    /// </remarks>
    [Fact]
    public async Task TheTablesWhoseColumnsMovedAreGoneAndTheOneWithItsOwnConceptIsNot()
    {
        var shape = await ShapeAsync();

        Assert.DoesNotContain(FoldedAway, shape.ContainsKey);
        Assert.True(shape.ContainsKey("character_expert_job"));
    }

    /// <remarks>
    /// Constraints are the part of a move that disappears quietly. A dropped foreign key refuses
    /// nothing and reports nothing; it shows up as an orphan row months later.
    /// <c>character_pvp_skill_state</c> declared a cascading foreign key besides its key, and it
    /// has to arrive on the table that absorbed it.
    /// </remarks>
    [Fact]
    public async Task TheCascadingForeignKeyArrivesOnTheTableThatAbsorbedIt()
    {
        var sql = await DeclarationAsync("character_pvp");

        Assert.Contains("FOREIGN KEY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REFERENCES characters", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DELETE CASCADE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <remarks>
    /// Every one of the six holds exactly one row per character, so every one of them is keyed by
    /// the character and nothing else.
    /// </remarks>
    [Theory]
    [InlineData("characters")]
    [InlineData("character_stats")]
    [InlineData("character_condition")]
    [InlineData("character_pvp")]
    [InlineData("character_settings")]
    [InlineData("character_expert_job")]
    public async Task ACharacterBodyTableIsKeyedByTheCharacterAlone(string table)
    {
        var sql = await DeclarationAsync(table);

        Assert.Contains("character_id INTEGER PRIMARY KEY", sql, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Two changes the contract did not name and the user authorised separately: a group and
    /// whether it was claimed become one row, and a table name stops being misspelled.
    /// </remarks>
    [Fact]
    public async Task TheTwoSeparatelyAuthorisedChangesTookEffect()
    {
        var shape = await ShapeAsync();

        Assert.True(shape.ContainsKey("character_daily_challenges"));
        Assert.Equal(
            new[] { "character_id", "claimed_at", "group_id", "group_index" },
            shape["character_daily_challenges"]);
        Assert.False(shape.ContainsKey("character_daily_challenge_groups"));
        Assert.False(shape.ContainsKey("character_daily_challenge_claims"));

        Assert.True(shape.ContainsKey("character_invisible_flags"));
        Assert.False(shape.ContainsKey("character_invisible_falgs"));
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ShapeAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-body-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());

            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();

            var tables = new List<string>();
            await using (var names = connection.CreateCommand())
            {
                names.CommandText =
                    "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                await using var reader = await names.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            var shape = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var table in tables)
            {
                var columns = new List<string>();
                await using var info = connection.CreateCommand();
                info.CommandText = $"PRAGMA table_info('{table}')";
                await using var reader = await info.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns.Add(reader.GetString(1));
                }

                columns.Sort(StringComparer.Ordinal);
                shape[table] = columns;
            }

            return shape;
        }
        finally
        {
            Release(path);
        }
    }

    private static async Task<string> DeclarationAsync(string table)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-decl-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());

            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@name";
            command.Parameters.AddWithValue("@name", table);

            return await command.ExecuteScalarAsync() as string
                ?? throw new InvalidOperationException($"{table} is not in the database at all.");
        }
        finally
        {
            Release(path);
        }
    }

    private static void Release(string path)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A file the runtime still holds is left for the temp directory to reap.
        }
    }
}
