using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Schema;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

public sealed class SqliteCharacterEntryStateRepositoryTests
{
    [Fact]
    public async Task CharacterEntryState_NewCharacterWithoutRequiredNormalizedRowsIsRejected()
    {
        var path = CreateDatabase();
        try
        {
            var repository = new CharacterSide(path).EntryState(new GoldLimitRuleService());
            await ExecuteAsync(path, """
                INSERT INTO characters(character_id, account_id, name, job, level)
                VALUES(21, 11, 'new', 1, 1);
                """);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.LoadAsync(
                    new AccountId(11), new CharacterId(21), CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CharacterEntryState_LoadsPopulatedNormalizedCrossSliceStateLosslessly()
    {
        var path = CreateDatabase();
        try
        {
            var repository = new CharacterSide(path).EntryState(new GoldLimitRuleService());
            var core = ItemCore(1001, 17);
            await ExecuteAsync(path, """
                INSERT INTO characters(character_id, account_id, name, job, level, clone_title_item_id,
                    user_state,manage_level)
                VALUES(22, 12, 'loaded', 1, 60, 77,10,13);
                INSERT INTO character_condition(character_id,stamina,fatigue_penalty,
                    champion_break_key_id,champion_break_mode,champion_break_value,
                    crystal_cube_type,crystal_cube_grade,
                    dimension_flag1,dimension_flag2,dimension_flag3,dimension_flag4,pc_room_state)
                    VALUES(22,7,8,-1234,1,99,1,255,1,2,3,4,0);
                INSERT INTO character_settings(character_id,user_state_bits,character_option_blob)
                    VALUES(22,5,X'0304');
                INSERT INTO character_pvp(character_id,pvp_grade,pvp_rating_grade,pvp_rank_point)
                    VALUES(22,8,9,99);
                INSERT INTO character_expert_job(character_id,expert_job_type,expert_job_exp)
                    VALUES(22,2,30);
                INSERT INTO character_stats(character_id,stat_hp_max,stat_physical_attack,
                    progress1,progress2,gold_carry_limit,auction_gold_limit)
                    VALUES(22,100,123,11,12,600000000,600000000);
                INSERT INTO character_dimensions VALUES(22,0,9,1,2);
                INSERT INTO character_hotkey_slots VALUES(22,0,20),(22,1,10);
                INSERT INTO character_skills VALUES(22,'pve',0,4,100,5,X'0908');
                INSERT INTO character_active_quests VALUES(22,2,200,6);
                INSERT INTO character_quest_notify_selections VALUES(22,0,201);
                INSERT INTO character_invisible_flags VALUES(22,200,1);
                INSERT INTO character_growth_weapon_stages VALUES(22,0,2);
                INSERT INTO character_daily_challenges(character_id,group_index,group_id,claimed_at)
                    VALUES(22,0,900,'2020-01-02 03:04:05');
                INSERT INTO character_daily_challenge_entries VALUES(22,0,0,901,10,20);
                INSERT INTO character_daily_challenge_tail_ids VALUES(22,0,902);
                INSERT INTO character_daily_schedule_states VALUES(22,0,1,2,3,4),(22,1,5,6,7,8),(22,2,9,10,11,12);
                INSERT INTO character_dungeon_permissions VALUES(22,0,300,2);
                INSERT INTO character_daily_counters(character_id,counter_key,period,value) VALUES(22,'revive_coin_daily_claim','day',1);
                INSERT INTO character_daily_counters(character_id,counter_key,period,value,last_consumed_at) VALUES(22,'lottery_double_reward_used','day',6,unixepoch()*1000);
                INSERT INTO items(item_uid,item_core,
                    creature_field04,creature_mode_flag,creature_progress_value,
                    creature_mode1_field0a,creature_mode1_field0b,creature_field_after_value,
                    creature_text,creature_tail_flag)
                    VALUES(400,@core,1,2,3,4,5,6,X'07',8);
                INSERT INTO item_slots(
                    owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                    VALUES('character',22,3,24,'world',400,400,1);
                INSERT INTO items(item_uid,item_core) VALUES(401,@core);
                INSERT INTO item_slots(
                    owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                    VALUES('character',22,19,1,'world',401,401,1);
                INSERT INTO character_achievement_complete VALUES(22,0,500,1,2,3,4);
                INSERT INTO character_collectbox_slots VALUES(22,3,0,700);
                UPDATE character_expert_job SET giveup_count=1, disjoint_machine_grade=3,
                    disjoint_machine_endurance=4, enchanter_endurance=5 WHERE character_id=22;
                INSERT INTO character_expert_job_recipes VALUES(22,70);
                INSERT INTO mailbox_messages(message_id,sender_character_id,sender_name,receiver_character_id,
                    title,body,mail_type,source_protocol,expire_at)
                    VALUES(800,77,'sender',22,'title','body',4,5,'9999-12-31 23:59:59');
                INSERT INTO mailbox_recipients(message_id,character_id,read_flag) VALUES(800,22,1);
                INSERT INTO mailbox_attachments(message_id,ordinal,item_type,item_template_id,item_kind,item_count,
                    item_core,detail_json) VALUES(800,0,2,1001,'equipment',3,@core,'detail');
                INSERT INTO mailbox_attachments(message_id,ordinal,item_template_id,asset_kind,gold_amount)
                    VALUES(800,1,0,'gold',123);
                INSERT INTO character_pvp_missions VALUES(22,0,900,12);
                """, ("@core", core));
            CharacterDatabaseSchema.Ensure($"Data Source={path}");

            var result = await repository.LoadAsync(
                new AccountId(12), new CharacterId(22), CancellationToken.None);

            Assert.Equal((byte)8, result.Profile.PvpGrade);
            Assert.Equal((byte)9, result.Profile.PvpRatingGrade);
            Assert.Equal((byte)10, result.Profile.UserState);
            Assert.Equal(123, result.Profile.DerivedStats.PhysicalAttack);
            Assert.Equal(13, result.Profile.ManageLevel);
            Assert.Empty(result.Profile.Pvp.Results);
            Assert.Equal(9, Assert.Single(result.Profile.Dimensions).Key);
            Assert.Equal(4, result.Profile.DimensionFlags.Flag4);
            var skill = Assert.Single(result.Skill.Skills);
            Assert.Equal((short)4, skill.Slot);
            Assert.Equal([9, 8], skill.ExtraValues);
            Assert.Equal(6, Assert.Single(result.Quest.ActiveQuests).TriggerValue);
            Assert.Equal((200, (byte)1), Assert.Single(result.Quest.ClearFlags));
            var daily = Assert.Single(result.Progression.DailyChallenges);
            Assert.Equal(20, Assert.Single(daily.Entries).ValueB);
            Assert.True(daily.Claimed);
            Assert.Equal(4, result.Progression.Schedules[0].ParamB);
            Assert.Equal(2, result.Progression.GoldLimitUpgradeLevel);
            Assert.Equal(4, Assert.Single(result.Features.Achievements).P4);
            Assert.Equal(core, Assert.Single(result.Features.TitleBooks).ItemCore);
            Assert.Equal(5, result.Features.ExpertJob.EnchanterEndurance);
            Assert.Equal([70], result.Features.ExpertJob.RecipeIds);
            Assert.Equal([20, 10], result.Profile.HotkeySlots);
            Assert.Equal(-1234, result.Progression.ChampionBreakKey);
            Assert.Equal(new[] { ((byte)0, 0), ((byte)1, 6), ((byte)3, 0), ((byte)5, 0), ((byte)6, 0) }, result.Progression.PremiumUsageSlots);
            Assert.Equal(1, result.Features.Mailbox.TotalCount);
            var message = Assert.Single(result.Features.Mailbox.PageMessages);
            Assert.Equal(800L, message.MessageId);
            Assert.Equal("sender", message.SenderName);
            Assert.Equal("body", message.Body);
            Assert.Equal(123, message.Gold);
            Assert.Equal(2, message.LetterState);
            Assert.Equal(1001, Assert.Single(message.Attachments).ItemTemplateId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    /// <summary>
    /// The gold upgrade level is worked out from the two limits rather than stored, and that
    /// working-out lives in the persistence implementation today.
    /// </summary>
    /// <remarks>
    /// Pinned here before the rule is moved, so that the move can be shown to change nothing. What
    /// was covered before was a single point -- both limits at six hundred million -- which fixed
    /// one of the five bands and could say nothing at all about the lower of the two deciding,
    /// because the two were equal. The rows below fix every band, both boundaries around each, and
    /// each of the two limits being the lower one.
    /// </remarks>
    [Theory]
    [InlineData(499_999_999, 900_000_000, 0)]
    [InlineData(500_000_000, 900_000_000, 1)]
    [InlineData(599_999_999, 900_000_000, 1)]
    [InlineData(600_000_000, 900_000_000, 2)]
    [InlineData(699_999_999, 900_000_000, 2)]
    [InlineData(700_000_000, 900_000_000, 3)]
    [InlineData(799_999_999, 900_000_000, 3)]
    [InlineData(800_000_000, 900_000_000, 4)]
    [InlineData(900_000_000, 499_999_999, 0)]
    [InlineData(900_000_000, 700_000_000, 3)]
    [InlineData(0, 0, 0)]
    public async Task CharacterEntryState_GoldUpgradeLevelFollowsTheLowerOfTheTwoLimits(
        int carry, int auction, int expected)
    {
        var path = CreateDatabase();
        try
        {
            var repository = new CharacterSide(path).EntryState(new GoldLimitRuleService());
            await ExecuteAsync(path, $"""
                INSERT INTO characters(character_id,account_id,name,job,level) VALUES(30,15,'gold',1,60);
                INSERT INTO character_condition(character_id,pc_room_state) VALUES(30,0);
                INSERT INTO character_stats(character_id) VALUES(30);
                INSERT INTO character_pvp(character_id) VALUES(30);
                INSERT INTO character_settings(character_id) VALUES(30);
                INSERT INTO character_expert_job(character_id) VALUES(30);
                UPDATE character_stats SET gold_carry_limit={carry}, auction_gold_limit={auction} WHERE character_id=30;
                """);

            var result = await repository.LoadAsync(
                new AccountId(15), new CharacterId(30), CancellationToken.None);

            Assert.Equal(expected, result.Progression.GoldLimitUpgradeLevel);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    /// <remarks>
    /// A character with no limits row at all reads as the lowest level rather than failing. Pinned
    /// alongside the rule because moving the rule out also moves what it is handed.
    /// </remarks>
    [Fact]
    public async Task CharacterEntryState_GoldUpgradeLevelIsLowestWhenNoLimitsAreRecorded()
    {
        var path = CreateDatabase();
        try
        {
            var repository = new CharacterSide(path).EntryState(new GoldLimitRuleService());
            await ExecuteAsync(path, """
                INSERT INTO characters(character_id,account_id,name,job,level) VALUES(31,16,'nogold',1,60);
                INSERT INTO character_condition(character_id,pc_room_state) VALUES(31,0);
                INSERT INTO character_stats(character_id) VALUES(31);
                INSERT INTO character_pvp(character_id) VALUES(31);
                INSERT INTO character_settings(character_id) VALUES(31);
                INSERT INTO character_expert_job(character_id) VALUES(31);
                """);

            var result = await repository.LoadAsync(
                new AccountId(16), new CharacterId(31), CancellationToken.None);

            Assert.Equal(0, result.Progression.GoldLimitUpgradeLevel);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CharacterEntryState_MailboxUsesOldestActiveInboxPageAndDistinctCount()
    {
        var path = CreateDatabase();
        try
        {
            var repository = new CharacterSide(path).EntryState(new GoldLimitRuleService());
            await ExecuteAsync(path, """
                INSERT INTO characters(character_id,account_id,name,job,level) VALUES(24,14,'mail',1,60);
                INSERT INTO character_condition(character_id,pc_room_state) VALUES(24,0);
                INSERT INTO character_stats(character_id) VALUES(24);
                INSERT INTO character_pvp(character_id) VALUES(24);
                INSERT INTO character_settings(character_id) VALUES(24);
                INSERT INTO character_expert_job(character_id) VALUES(24);

                WITH RECURSIVE seq(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM seq WHERE n<22)
                INSERT INTO mailbox_messages(message_id,receiver_character_id,created_at,expire_at)
                    SELECT 100+n,24,printf('2026-01-%02d 00:00:00',n),'2099-01-01 00:00:00' FROM seq;
                INSERT INTO mailbox_recipients(message_id,character_id)
                    SELECT message_id,24 FROM mailbox_messages WHERE message_id BETWEEN 101 AND 122;

                INSERT INTO mailbox_messages(message_id,receiver_character_id,created_at,expire_at)
                    VALUES
                    (201,24,'2025-01-01 00:00:00','2099-01-01 00:00:00'),
                    (202,24,'2025-01-02 00:00:00','2099-01-01 00:00:00'),
                    (203,24,'2025-01-03 00:00:00','2099-01-01 00:00:00'),
                    (204,24,'2025-01-04 00:00:00','2000-01-01 00:00:00'),
                    (205,24,'2026-03-01 00:00:00','2000-01-01 00:00:00'),
                    (206,24,'2025-01-05 00:00:00','2099-01-01 00:00:00');
                UPDATE mailbox_messages SET unlimited_flag=1 WHERE message_id=205;
                INSERT INTO mailbox_recipients(message_id,character_id,folder,saved_flag,deleted_flag)
                    VALUES
                    (201,24,0,1,0),
                    (202,24,1,0,0),
                    (203,24,0,0,1),
                    (204,24,0,0,0),
                    (205,24,0,0,0);
                """);

            var result = await repository.LoadAsync(
                new AccountId(14), new CharacterId(24), CancellationToken.None);

            Assert.Equal(23, result.Features.Mailbox.TotalCount);
            Assert.Equal(
                Enumerable.Range(101, 20).Reverse().Select(value => (long)value),
                result.Features.Mailbox.PageMessages.Select(value => value.MessageId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CharacterEntryState_UpgradesPreProjectionNormalizedSchemaIdempotently()
    {
        var path = CreateDatabase();
        try
        {
            await ExecuteAsync(path, """
                CREATE TABLE characters(
                    character_id INTEGER PRIMARY KEY, account_id INTEGER NOT NULL,
                    name TEXT NOT NULL, job INTEGER NOT NULL DEFAULT 0,
                    level INTEGER NOT NULL DEFAULT 1);
                CREATE TABLE mailbox_messages(
                    message_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    receiver_character_id INTEGER NOT NULL,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    unlimited_flag INTEGER NOT NULL DEFAULT 0,
                    expire_at TEXT NOT NULL DEFAULT '9999-12-31 23:59:59',
                    deleted_by_sender INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE mailbox_recipients(
                    recipient_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    message_id INTEGER NOT NULL, character_id INTEGER NOT NULL,
                    folder INTEGER NOT NULL DEFAULT 0, saved_flag INTEGER NOT NULL DEFAULT 0,
                    deleted_flag INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(message_id,character_id,folder));
                """);

            // Twice, because bringing the database into shape has to be able to happen again
            // over a database already in it.
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await ExecuteAsync(path, """
                INSERT INTO characters(character_id,account_id,name,job,level) VALUES(25,15,'upgrade',1,60);
                INSERT INTO character_condition(character_id,pc_room_state) VALUES(25,0);
                INSERT INTO character_stats(character_id) VALUES(25);
                INSERT INTO character_pvp(character_id) VALUES(25);
                INSERT INTO character_settings(character_id) VALUES(25);
                INSERT INTO character_expert_job(character_id) VALUES(25);
                INSERT INTO mailbox_messages(message_id,receiver_character_id) VALUES(900,25);
                INSERT INTO mailbox_recipients(message_id,character_id) VALUES(900,25);
                """);

            var result = await new CharacterSide(path).EntryState(new GoldLimitRuleService()).LoadAsync(
                new AccountId(15), new CharacterId(25), CancellationToken.None);

            Assert.Equal(0, result.Profile.ManageLevel);
            var message = Assert.Single(result.Features.Mailbox.PageMessages);
            Assert.Equal(900, message.MessageId);
            Assert.Equal(string.Empty, message.SenderName);
            Assert.Equal(1, message.LetterState);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("missing-required")]
    [InlineData("invalid-skill")]
    [InlineData("group-out-of-range")]
    [InlineData("incomplete-schedule")]
    public async Task CharacterEntryState_RejectsInvalidNormalizedState(string invalidCase)
    {
        var path = CreateDatabase();
        try
        {
            var repository = new CharacterSide(path).EntryState(new GoldLimitRuleService());
            await ExecuteAsync(path, """
                INSERT INTO characters(character_id,account_id,name,job,level) VALUES(23,13,'bad',1,60);
                INSERT INTO character_condition(character_id,pc_room_state) VALUES(23,0);
                INSERT INTO character_stats(character_id) VALUES(23);
                INSERT INTO character_pvp(character_id) VALUES(23);
                INSERT INTO character_settings(character_id) VALUES(23);
                INSERT INTO character_expert_job(character_id) VALUES(23);
                """);
            var sql = invalidCase switch
            {
                "wrong-owner" => "UPDATE characters SET account_id=99 WHERE character_id=23;",
                "missing-required" => "DELETE FROM character_stats WHERE character_id=23;",
                "invalid-skill" => "INSERT INTO character_skills VALUES(23,'pve',0,0,-1,1,NULL);",
                "group-out-of-range" => "INSERT INTO character_daily_challenges(character_id,group_index,group_id) VALUES(23,9,1);",
                _ => "INSERT INTO character_daily_schedule_states VALUES(23,0,0,0,0,0);",
            };
            await ExecuteAsync(path, sql);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.LoadAsync(new AccountId(13), new CharacterId(23), CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    /// <summary>
    /// A stored value too wide for the field the packet gives it is refused when the character is
    /// loaded, not when the packet is being written.
    /// </summary>
    /// <remarks>
    /// The refusal is the reason the encoder may narrow without fear, and it is deliberate that it
    /// happens here: a character whose stored state cannot be sent is turned away with one message
    /// rather than failing halfway through a packet that is already partly written.
    ///
    /// Nothing asserted it. That is how four of these checks came to be dropped in the change that
    /// regrouped the profile by concept -- the four fields that moved out of the flattened record
    /// were the four whose checks had nothing left to hang off, and losing a refusal shows only
    /// when a value is out of range, which no fixture had. The four are the last four rows here.
    /// </remarks>
    [Theory]
    [InlineData("UPDATE character_condition SET stamina=256 WHERE character_id=23;", Profile)]
    [InlineData("UPDATE character_condition SET mood_value=65536 WHERE character_id=23;", Profile)]
    [InlineData("UPDATE character_pvp SET pvp_rank_point=4294967296 WHERE character_id=23;", Profile)]
    [InlineData("UPDATE characters SET manage_level=256 WHERE character_id=23;", Extended)]
    [InlineData("UPDATE characters SET name_tag_item_id=4294967296 WHERE character_id=23;", Extended)]
    [InlineData("UPDATE characters SET name_tag_expire_time=4294967296 WHERE character_id=23;", Extended)]
    [InlineData("UPDATE characters SET guild_power_war=4294967296 WHERE character_id=23;", Extended)]
    public async Task CharacterEntryState_RefusesAValueTooWideForItsFieldWhenItIsLoaded(
        string overflow, string refusal)
    {
        var path = CreateDatabase();
        try
        {
            var repository = new CharacterSide(path).EntryState(new GoldLimitRuleService());
            await ExecuteAsync(path, """
                INSERT INTO characters(character_id,account_id,name,job,level) VALUES(23,13,'wide',1,60);
                INSERT INTO character_condition(character_id,pc_room_state) VALUES(23,0);
                INSERT INTO character_stats(character_id) VALUES(23);
                INSERT INTO character_pvp(character_id) VALUES(23);
                INSERT INTO character_settings(character_id) VALUES(23);
                INSERT INTO character_expert_job(character_id) VALUES(23);
                """);

            // The same character loads before the value is widened, so the refusal below is about
            // the value and not about anything else this fixture leaves out.
            await repository.LoadAsync(new AccountId(13), new CharacterId(23), CancellationToken.None);
            await ExecuteAsync(path, overflow);

            // Which of the two refusals fired is part of the answer: they are worded for two
            // different halves of the profile, and a check moved into the wrong one would still
            // refuse the character while telling the reader the wrong thing about why.
            var refused = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.LoadAsync(
                    new AccountId(13), new CharacterId(23), CancellationToken.None));
            Assert.Equal(refusal, refused.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private const string Profile = "Character public profile state is out of range.";

    private const string Extended = "Character extended detail state is out of range.";

    private static byte[] ItemCore(int itemId, int value)
    {
        var core = new byte[82];
        BitConverter.GetBytes(itemId).CopyTo(core, 1);
        BitConverter.GetBytes(value).CopyTo(core, 5);
        return core;
    }

    private static async Task ExecuteAsync(
        string path,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateDatabase() =>
        Path.Combine(Path.GetTempPath(), $"dfo-character-entry-{Guid.NewGuid():N}.db");
}
