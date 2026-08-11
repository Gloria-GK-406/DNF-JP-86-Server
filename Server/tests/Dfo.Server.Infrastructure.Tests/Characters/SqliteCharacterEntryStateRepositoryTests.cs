using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Infrastructure.Characters;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Tests.Characters;

public sealed class SqliteCharacterEntryStateRepositoryTests
{
    [Fact]
    public async Task CharacterEntryState_NewCharacterWithoutRequiredNormalizedRowsIsRejected()
    {
        var path = CreateDatabase();
        try
        {
            var repository = new SqliteCharacterEntryStateRepository(path);
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
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CharacterEntryState_LoadsPopulatedNormalizedCrossSliceStateLosslessly()
    {
        var path = CreateDatabase();
        try
        {
            var repository = new SqliteCharacterEntryStateRepository(path);
            var core = ItemCore(1001, 17);
            await ExecuteAsync(path, """
                INSERT INTO characters(character_id, account_id, name, job, level, clone_title_item_id, appearance_blob)
                VALUES(22, 12, 'loaded', 1, 60, 77, X'0102');
                INSERT INTO character_subtype0_fields(character_id,stamina,fatigue_penalty,
                    expert_job_type,expert_job_exp,user_state_bits,pvp_rank_point)
                    VALUES(22,7,8,2,30,5,99);
                INSERT INTO character_subtype1_fields(character_id,stat_hp_max,stat_physical_attack,
                    skill_tree_index,progress1,progress2) VALUES(22,100,123,0,11,12);
                INSERT INTO character_dimensions VALUES(22,0,9,1,2);
                INSERT INTO character_dimension_flags VALUES(22,1,2,3,4);
                INSERT INTO character_init_flags(character_id,champion_break_key_id,
                    champion_break_mode,champion_break_value,character_option_blob)
                    VALUES(22,-1234,1,99,X'0304');
                INSERT INTO character_hotkey_slots VALUES(22,0,20),(22,1,10);
                INSERT INTO character_skills VALUES(22,0,4,100,5,X'0908');
                INSERT INTO character_active_quests VALUES(22,2,200,6);
                INSERT INTO character_quest_notify_selections VALUES(22,0,201);
                INSERT INTO character_invisible_falgs VALUES(22,200,1);
                INSERT INTO character_growth_weapon_stages VALUES(22,0,2);
                INSERT INTO character_crystal_contract VALUES(22,1,255);
                INSERT INTO character_gold_limits VALUES(22,600000000,600000000);
                INSERT INTO character_daily_challenge_groups VALUES(22,0,900);
                INSERT INTO character_daily_challenge_entries VALUES(22,0,0,901,10,20);
                INSERT INTO character_daily_challenge_claims(character_id,group_index) VALUES(22,0);
                INSERT INTO character_daily_challenge_tail_ids VALUES(22,0,902);
                INSERT INTO character_daily_schedule_states VALUES(22,0,1,2,3,4),(22,1,5,6,7,8),(22,2,9,10,11,12);
                INSERT INTO character_dungeon_permissions VALUES(22,0,300,2);
                INSERT INTO character_daily_counters VALUES(22,'revive_coin_daily_claim','day',1);
                INSERT INTO character_creatures VALUES(22,0,400,1,2,3,4,5,6,X'07',8);
                INSERT INTO character_new_titlebook(character_id,category,slot_index,item_core)
                    VALUES(22,0,1,@core);
                INSERT INTO character_achievement_complete VALUES(22,0,500,1,2,3,4);
                INSERT INTO character_collectbox_slots VALUES(22,3,0,700);
                INSERT INTO character_expert_job VALUES(22,1,3,4,5);
                INSERT INTO character_expert_job_recipes VALUES(22,70);
                INSERT INTO mailbox_messages(message_id,receiver_character_id,expire_at) VALUES(800,22,'9999-12-31 23:59:59');
                INSERT INTO mailbox_recipients(message_id,character_id) VALUES(800,22);
                INSERT INTO character_pvp_missions VALUES(22,0,900,12);
                """, ("@core", core));

            var result = await repository.LoadAsync(
                new AccountId(12), new CharacterId(22), CancellationToken.None);

            Assert.Equal([1, 2], result.Profile.AppearanceBlob);
            Assert.Equal(123, result.Profile.Subtype1.PhysicalAttack);
            Assert.Equal(9, Assert.Single(result.Profile.Dimensions).Key);
            Assert.Equal(4, result.Profile.DimensionFlags.Flag4);
            var skill = Assert.Single(result.Skill.Skills);
            Assert.Equal((short)4, skill.Slot);
            Assert.Equal([9, 8], skill.ExtraValues);
            Assert.Equal(6, Assert.Single(result.Quest.ActiveQuests).TriggerValue);
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
            Assert.Equal(1, result.Features.Mailbox.TotalCount);
            Assert.Equal([800L], result.Features.Mailbox.PageMessageIds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CharacterEntryState_MailboxUsesOldestActiveInboxPageAndDistinctCount()
    {
        var path = CreateDatabase();
        try
        {
            var repository = new SqliteCharacterEntryStateRepository(path);
            await ExecuteAsync(path, """
                INSERT INTO characters(character_id,account_id,name,job,level) VALUES(24,14,'mail',1,60);
                INSERT INTO character_subtype0_fields(character_id) VALUES(24);
                INSERT INTO character_subtype1_fields(character_id) VALUES(24);
                INSERT INTO character_init_flags(character_id) VALUES(24);

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
                result.Features.Mailbox.PageMessageIds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("missing-required")]
    [InlineData("invalid-skill")]
    [InlineData("oversized-appearance")]
    [InlineData("orphan-claim")]
    [InlineData("incomplete-schedule")]
    public async Task CharacterEntryState_RejectsInvalidNormalizedState(string invalidCase)
    {
        var path = CreateDatabase();
        try
        {
            var repository = new SqliteCharacterEntryStateRepository(path);
            await ExecuteAsync(path, """
                INSERT INTO characters(character_id,account_id,name,job,level) VALUES(23,13,'bad',1,60);
                INSERT INTO character_subtype0_fields(character_id) VALUES(23);
                INSERT INTO character_subtype1_fields(character_id) VALUES(23);
                INSERT INTO character_init_flags(character_id) VALUES(23);
                """);
            var sql = invalidCase switch
            {
                "wrong-owner" => "UPDATE characters SET account_id=99 WHERE character_id=23;",
                "missing-required" => "DELETE FROM character_subtype1_fields WHERE character_id=23;",
                "invalid-skill" => "INSERT INTO character_skills VALUES(23,0,0,-1,1,NULL);",
                "oversized-appearance" => "UPDATE characters SET appearance_blob=zeroblob(16385) WHERE character_id=23;",
                "orphan-claim" => "INSERT INTO character_daily_challenge_claims(character_id,group_index) VALUES(23,0);",
                _ => "INSERT INTO character_daily_schedule_states VALUES(23,0,0,0,0,0);",
            };
            await ExecuteAsync(path, sql);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.LoadAsync(new AccountId(13), new CharacterId(23), CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

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
