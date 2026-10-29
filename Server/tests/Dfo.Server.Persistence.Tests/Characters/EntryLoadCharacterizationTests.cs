using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Schema;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

/// <summary>
/// Everything a character entering the game is loaded with, written down before it is moved.
/// </summary>
/// <remarks>
/// The load reads thirty-three tables and assembles one record. When that assembly moves out of
/// the persistence implementation, the danger is not that it stops working -- it is that a field
/// or the order of a collection quietly changes and nothing says so. The frame-level golden
/// covers the bytes that finally reach the client; it says nothing about this record, which is
/// several transformations upstream of them.
///
/// So the whole record is written out and compared as text. Any field that changes value, any
/// collection whose members reorder, any property added or removed, fails this and says where.
/// </remarks>
public sealed class EntryLoadCharacterizationTests
{
    [Fact]
    public async Task ACharacterIsLoadedWithExactlyWhatItWasLoadedWithBefore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-pin-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CharacterSide(path).EntryState(new GoldLimitRuleService());
            await SeedAsync(path);
            CharacterDatabaseSchema.Ensure($"Data Source={path}");

            var loaded = await repository.LoadAsync(
                new AccountId(12), new CharacterId(22), CancellationToken.None);

            Assert.Equal(Expected.ReplaceLineEndings("\n") + "\n", StorageRecordDump.Of(loaded));
        }
        finally
        {
            Clean(path);
        }
    }

    /// <summary>The two tables the record above cannot reach.</summary>
    /// <remarks>
    /// A dark knight's combo pages and a knight's shield deck are refused outright for any other
    /// job -- the load throws rather than ignoring them -- so no single character can pin them
    /// alongside the rest. They are pinned here instead, against the job each belongs to, because
    /// a table left unpinned is a table the move can quietly drop.
    /// </remarks>
    [Theory]
    [InlineData(9, "INSERT INTO character_dark_knight_combo_skill_pages VALUES(22,0,X'0A0B');",
        "Job = 9\nBonusSp = 0\nBonusTp = 0\nSkills = []\n"
        + "DarkKnightComboPages[0].Item1 = 0\nDarkKnightComboPages[0].Item2 = 0x0A0B\n"
        + "KnightShieldSlots = []\n")]
    [InlineData(12, "INSERT INTO character_knight_shield_deck VALUES(22,0,666);",
        "Job = 12\nBonusSp = 0\nBonusTp = 0\nSkills = []\nDarkKnightComboPages = []\n"
        + "KnightShieldSlots[0].Item1 = 0\nKnightShieldSlots[0].Item2 = 666\n")]
    public async Task ATableOnlyOneJobCanHoldIsLoadedForThatJob(byte job, string row, string expected)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-job-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CharacterSide(path).EntryState(new GoldLimitRuleService());
            await ExecuteAsync(
                path,
                "INSERT INTO characters(character_id,account_id,name,job,level) "
                + $"VALUES(22,12,'job',{job},60);"
                + "INSERT INTO character_stats(character_id) VALUES(22);"
                + "INSERT INTO character_condition(character_id,pc_room_state) VALUES(22,0);"
                + "INSERT INTO character_pvp(character_id) VALUES(22);"
                + "INSERT INTO character_settings(character_id) VALUES(22);"
                + "INSERT INTO character_expert_job(character_id) VALUES(22);"
                + row);

            var loaded = await repository.LoadAsync(
                new AccountId(12), new CharacterId(22), CancellationToken.None);

            // The whole section rather than a substring: a row that appears alongside the expected
            // one is a difference too, and a substring cannot see it.
            Assert.Equal(expected.ReplaceLineEndings("\n"), StorageRecordDump.Of(loaded.Skill));
        }
        finally
        {
            Clean(path);
        }
    }

    /// <summary>One row in every table the load reads that can hold one.</summary>
    private static async Task SeedAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO characters(character_id,account_id,name,name_bytes,job,grow_type,level,clone_title_item_id,slot_index,delete_flag,town_id,area_id,pos_x,pos_y,direction,area_state,bonus_sp,bonus_tp,exp,ex_equip_slot_stat,user_state,name_tag_item_id,name_tag_expire_time,manage_level,guild_power_war,guild_level)
                VALUES(22,12,'loaded',X'00A0',8,1,60,1001,0,0,101,102,1002,1003,103,104,1004,1005,1006,1007,107,1044,1045,132,1047,115);
            INSERT INTO character_stats(character_id,stat_hp_max,stat_mp_max,stat_physical_attack,stat_physical_defense,stat_magical_attack,stat_magical_defense,stat_move_speed,stat_attack_speed,stat_cast_speed,stat_inventory_limit,stat_fire_resistance,stat_water_resistance,stat_dark_resistance,stat_light_resistance,stat_hp_regen_speed,stat_mp_regen_speed,stat_hit_recovery,stat_jump_power,stat_weight,progress1,progress2,current_hp_mp_percent,equipped_creature_level,equip_list_trailing,flag_byte,server_timestamp,quest_shop_count,gold_carry_limit,auction_gold_limit)
                VALUES(22,1023,1024,1025,1026,1027,1028,1029,1030,1031,1032,1035,1036,1037,1038,1039,1040,1041,1042,1043,1033,1034,129,130,1046,131,1048,1049,600000000,600000000);
            INSERT INTO character_condition(character_id,stamina,fatigue_penalty,fatigue_update,fatigue_display_update,is_event_character,is_hardcore_mode,is_hardcore_dead,hardcore_death_count,is_private_store,chat_ban_end_time,mood_value,black_count,chaos_point,trailing_byte,creature_field1,creature_field2,creature_field3,creature_field4,creature_buffer,pc_room_id,is_premium_pc_room,server_group_id,channel_display_mode,channel_type,is_return_user,return_user_flag,disguise_kind,is_disguised,emotion_index,action_byte,costume_flag,aura_flag,pet_display_flag,title_display_flag,link_slot_enabled,link_type_a,link_type_b,pc_room_state,champion_break_key_id,champion_break_mode,champion_break_value,crystal_cube_type,crystal_cube_grade,dimension_flag1,dimension_flag2,dimension_flag3,dimension_flag4)
                VALUES(22,108,1008,1018,1021,0,0,1,1010,1,1012,1013,1016,1017,128,110,111,112,113,X'0102030405060708',1015,0,114,1019,118,0,117,116,1,1020,121,122,123,124,125,1,119,120,1053,1050,1051,1052,1,7,1,2,3,4);
            INSERT INTO character_pvp(character_id,skills_initialized,pvp_grade,pvp_rating_grade,pvp_rank_point,pvp_stat_a,pvp_win_streak,pvp_lose_streak)
                VALUES(22,0,105,106,1014,1022,126,127);
            INSERT INTO character_settings(character_id,character_option_blob,user_state_bits)
                VALUES(22,X'00A2',1011);
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
            INSERT INTO character_daily_schedule_states
                VALUES(22,0,1,2,3,4),(22,1,5,6,7,8),(22,2,9,10,11,12);
            INSERT INTO character_dungeon_permissions VALUES(22,0,300,2);
            INSERT INTO character_daily_counters(character_id,counter_key,period,value) VALUES(22,'revive_coin_daily_claim',date('now','+2 hours'),1);
            INSERT INTO character_daily_counters(character_id,counter_key,period,value,last_consumed_at) VALUES(22,'lottery_double_reward_used','day',6,unixepoch()*1000);
            INSERT INTO items(item_uid,item_core,
                creature_field04,creature_mode_flag,creature_progress_value,
                creature_mode1_field0a,creature_mode1_field0b,creature_field_after_value,
                creature_text,creature_tail_flag)
                VALUES(400,@core,1,2,3,4,5,6,X'07',8);
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                VALUES('character',22,3,24,'world',400,400,1);
            INSERT INTO items(item_uid,item_core) VALUES(401,@core);
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                VALUES('character',22,19,1,'world',401,401,1);
            INSERT INTO character_achievement_complete VALUES(22,0,500,1,2,3,4);
            INSERT INTO character_collectbox_slots VALUES(22,3,0,700);
            INSERT INTO character_expert_job(character_id,giveup_count,disjoint_machine_grade,disjoint_machine_endurance,enchanter_endurance,expert_job_type,expert_job_exp)
                VALUES(22,1,3,4,5,109,1009);
            INSERT INTO character_expert_job_recipes VALUES(22,70);
            INSERT INTO mailbox_messages(message_id,sender_character_id,sender_name,
                receiver_character_id,title,body,mail_type,source_protocol,expire_at,created_at)
                VALUES(800,77,'sender',22,'title','body',4,5,'9999-12-31 23:59:59',
                    '2020-01-02 03:04:05');
            INSERT INTO mailbox_recipients(message_id,character_id,read_flag) VALUES(800,22,1);
            INSERT INTO mailbox_attachments(attachment_id,message_id,ordinal,item_type,source_list_type,source_slot_index,source_item_uid,item_template_id,item_kind,item_count,instance_value,durability,seal_flag,option_value,expire_time,marker_16,pet_serial_or_handle,item_core,detail_json)
                VALUES(1054,800,0,1055,1056,1057,1058,1059,'equipment',1060,1061,1062,1063,1064,1065,1066,1067,@core,'detail');
            INSERT INTO mailbox_attachments(attachment_id,message_id,ordinal,item_template_id,asset_kind,gold_amount)
                VALUES(1055,800,1,0,'gold',123);
            INSERT INTO character_pvp_missions VALUES(22,0,900,12);
            """;
        command.Parameters.AddWithValue("@core", ItemCore(1001, 17));
        await command.ExecuteNonQueryAsync();
    }


    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static void Clean(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(path + suffix);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }
        }
    }

    private static byte[] ItemCore(int itemId, int value)
    {
        var core = new byte[82];
        BitConverter.GetBytes(itemId).CopyTo(core, 1);
        BitConverter.GetBytes(value).CopyTo(core, 5);
        return core;
    }

    /// <summary>What the load produced before any of it moved.</summary>
    private const string Expected =
        """
        AccountId.Value = 12
        CharacterId.Value = 22
        Profile.Job = 8
        Profile.Level = 60
        Profile.CloneTitleItemId = 1001
        Profile.Condition.Stamina = 108
        Profile.Condition.FatiguePenalty = 1008
        Profile.Condition.EventCharacter = 0
        Profile.Condition.PrivateStore = 1
        Profile.Condition.HardcoreMode = 0
        Profile.Condition.HardcoreDead = 1
        Profile.Condition.HardcoreDeathCount = 1010
        Profile.Condition.ChatBanEndTime = 1012
        Profile.Condition.MoodValue = 1013
        Profile.Condition.CreatureField1 = 110
        Profile.Condition.CreatureField2 = 111
        Profile.Condition.CreatureField3 = 112
        Profile.Condition.CreatureField4 = 113
        Profile.Condition.CreatureBuffer = 0x0102030405060708
        Profile.Condition.PcRoomId = 1015
        Profile.Condition.PremiumPcRoom = 0
        Profile.Condition.ServerGroupId = 114
        Profile.Condition.BlackCount = 1016
        Profile.Condition.ChaosPoint = 1017
        Profile.Condition.DisguiseKind = 116
        Profile.Condition.Disguised = 1
        Profile.Condition.FatigueUpdate = 1018
        Profile.Condition.ReturnUserFlag = 117
        Profile.Condition.ChannelDisplayMode = 1019
        Profile.Condition.ChannelType = 118
        Profile.Condition.ReturnUser = 0
        Profile.Condition.LinkSlotEnabled = 1
        Profile.Condition.LinkTypeA = 119
        Profile.Condition.LinkTypeB = 120
        Profile.Condition.EmotionIndex = 1020
        Profile.Condition.ActionByte = 121
        Profile.Condition.FatigueDisplayUpdate = 1021
        Profile.Condition.CostumeFlag = 122
        Profile.Condition.AuraFlag = 123
        Profile.Condition.PetDisplayFlag = 124
        Profile.Condition.TitleDisplayFlag = 125
        Profile.Condition.TrailingByte = 128
        Profile.Settings.UserStateBits = 1011
        Profile.Pvp.RankPoint = 1014
        Profile.Pvp.StatA = 1022
        Profile.Pvp.WinStreak = 126
        Profile.Pvp.LoseStreak = 127
        Profile.Pvp.Results = []
        Profile.ExpertJob.Type = 109
        Profile.ExpertJob.Experience = 1009
        Profile.DerivedStats.HpMaximum = 1023
        Profile.DerivedStats.MpMaximum = 1024
        Profile.DerivedStats.PhysicalAttack = 1025
        Profile.DerivedStats.PhysicalDefense = 1026
        Profile.DerivedStats.MagicalAttack = 1027
        Profile.DerivedStats.MagicalDefense = 1028
        Profile.DerivedStats.MoveSpeed = 1029
        Profile.DerivedStats.AttackSpeed = 1030
        Profile.DerivedStats.CastSpeed = 1031
        Profile.DerivedStats.InventoryLimit = 1032
        Profile.DerivedStats.Progress1 = 1033
        Profile.DerivedStats.Progress2 = 1034
        Profile.DerivedStats.FireResistance = 1035
        Profile.DerivedStats.WaterResistance = 1036
        Profile.DerivedStats.DarkResistance = 1037
        Profile.DerivedStats.LightResistance = 1038
        Profile.DerivedStats.HpRegenSpeed = 1039
        Profile.DerivedStats.MpRegenSpeed = 1040
        Profile.DerivedStats.HitRecovery = 1041
        Profile.DerivedStats.JumpPower = 1042
        Profile.DerivedStats.Weight = 1043
        Profile.DerivedStats.CurrentHpMpPercent = 129
        Profile.DerivedStats.EquippedCreatureLevel = 130
        Profile.DerivedStats.EquipListTrailing = 1046
        Profile.DerivedStats.FlagByte = 131
        Profile.DerivedStats.ServerTimestamp = 1048
        Profile.DerivedStats.QuestShopCount = 1049
        Profile.Guild.Level = 115
        Profile.Guild.PowerWar = 1047
        Profile.SkillTreeIndex = -1
        Profile.NameTagItemId = 1044
        Profile.NameTagExpireTime = 1045
        Profile.ManageLevel = 132
        Profile.HotkeySlots[0] = 20
        Profile.HotkeySlots[1] = 10
        Profile.CharacterOptionBlob = 0x00A2
        Profile.CharacterExperience = 1006
        Profile.ExtraEquipmentSlotState = 1007
        Profile.Dimensions[0].Item1 = 0
        Profile.Dimensions[0].Item2 = 9
        Profile.Dimensions[0].Item3 = 1
        Profile.Dimensions[0].Item4 = 2
        Profile.DimensionFlags.Item1 = 1
        Profile.DimensionFlags.Item2 = 2
        Profile.DimensionFlags.Item3 = 3
        Profile.DimensionFlags.Item4 = 4
        Profile.PvpGrade = 105
        Profile.PvpRatingGrade = 106
        Profile.UserState = 107
        Skill.Job = 8
        Skill.BonusSp = 1004
        Skill.BonusTp = 1005
        Skill.Skills[0].PageIndex = 0
        Skill.Skills[0].Slot = 4
        Skill.Skills[0].SkillId = 100
        Skill.Skills[0].Level = 5
        Skill.Skills[0].ExtraValues = 0x0908
        Skill.DarkKnightComboPages = []
        Skill.KnightShieldSlots = []
        Quest.ActiveQuests[0].Slot = 2
        Quest.ActiveQuests[0].QuestId = 200
        Quest.ActiveQuests[0].TriggerValue = 6
        Quest.NotificationQuestIds[0] = 201
        Quest.ClearFlags[0].Item1 = 200
        Quest.ClearFlags[0].Item2 = 1
        Progression.Level = 60
        Progression.GrowthWeaponStages[0] = 2
        Progression.CubeType = 1
        Progression.CubeGrade = 7
        Progression.ChampionBreakKey = 1050
        Progression.ChampionBreakMode = 1051
        Progression.ChampionBreakValue = 1052
        Progression.GoldLimitUpgradeLevel = 2
        Progression.DailyChallenges[0].GroupIndex = 0
        Progression.DailyChallenges[0].GroupId = 900
        Progression.DailyChallenges[0].Entries[0].Item1 = 0
        Progression.DailyChallenges[0].Entries[0].Item2 = 901
        Progression.DailyChallenges[0].Entries[0].Item3 = 10
        Progression.DailyChallenges[0].Entries[0].Item4 = 20
        Progression.DailyChallenges[0].Claimed = True
        Progression.DailyChallengeTailIds[0] = 902
        Progression.Schedules[0].Item1 = 0
        Progression.Schedules[0].Item2 = 1
        Progression.Schedules[0].Item3 = 2
        Progression.Schedules[0].Item4 = 3
        Progression.Schedules[0].Item5 = 4
        Progression.Schedules[1].Item1 = 1
        Progression.Schedules[1].Item2 = 5
        Progression.Schedules[1].Item3 = 6
        Progression.Schedules[1].Item4 = 7
        Progression.Schedules[1].Item5 = 8
        Progression.Schedules[2].Item1 = 2
        Progression.Schedules[2].Item2 = 9
        Progression.Schedules[2].Item3 = 10
        Progression.Schedules[2].Item4 = 11
        Progression.Schedules[2].Item5 = 12
        Progression.Dungeons[0].Item1 = 0
        Progression.Dungeons[0].Item2 = 300
        Progression.Dungeons[0].Item3 = 2
        Progression.PremiumUsageSlots[0].Item1 = 0
        Progression.PremiumUsageSlots[0].Item2 = 0
        Progression.PremiumUsageSlots[1].Item1 = 1
        Progression.PremiumUsageSlots[1].Item2 = 6
        Progression.PremiumUsageSlots[2].Item1 = 3
        Progression.PremiumUsageSlots[2].Item2 = 0
        Progression.PremiumUsageSlots[3].Item1 = 5
        Progression.PremiumUsageSlots[3].Item2 = 0
        Progression.PremiumUsageSlots[4].Item1 = 6
        Progression.PremiumUsageSlots[4].Item2 = 0
        Progression.DailyReviveCoinClaimed = True
        Features.Pets[0].SortOrder = 0
        Features.Pets[0].CreatureKey = 400
        Features.Pets[0].Field04 = 1
        Features.Pets[0].ModeFlag = 2
        Features.Pets[0].ProgressValue = 3
        Features.Pets[0].Mode1Field0A = 4
        Features.Pets[0].Mode1Field0B = 5
        Features.Pets[0].FieldAfterValue = 6
        Features.Pets[0].CreatureText = 0x07
        Features.Pets[0].TailFlag = 8
        Features.TitleBooks[0].Category = 0
        Features.TitleBooks[0].SlotIndex = 1
        Features.TitleBooks[0].ItemCore = 0x00E90300001100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000
        Features.Achievements[0].SortOrder = 0
        Features.Achievements[0].AchievementId = 500
        Features.Achievements[0].P1 = 1
        Features.Achievements[0].P2 = 2
        Features.Achievements[0].P3 = 3
        Features.Achievements[0].P4 = 4
        Features.CollectionSlots[0].Item1 = 3
        Features.CollectionSlots[0].Item2 = 0
        Features.CollectionSlots[0].Item3 = 700
        Features.ExpertJob.Item1 = 109
        Features.ExpertJob.Item2 = 1
        Features.ExpertJob.Item3 = 3
        Features.ExpertJob.Item4 = 4
        Features.ExpertJob.Item5 = 5
        Features.ExpertJob.Item6[0] = 70
        Features.Mailbox.TotalCount = 1
        Features.Mailbox.PageMessages[0].MessageId = 800
        Features.Mailbox.PageMessages[0].SenderCharacterId = 77
        Features.Mailbox.PageMessages[0].SenderName = "sender"
        Features.Mailbox.PageMessages[0].MailType = 4
        Features.Mailbox.PageMessages[0].SourceProtocol = 5
        Features.Mailbox.PageMessages[0].Title = "title"
        Features.Mailbox.PageMessages[0].Body = "body"
        Features.Mailbox.PageMessages[0].Gold = 123
        Features.Mailbox.PageMessages[0].RemainingSeconds = 0
        Features.Mailbox.PageMessages[0].CreatedAtUnixSeconds = 1577934245
        Features.Mailbox.PageMessages[0].LetterState = 2
        Features.Mailbox.PageMessages[0].Attachments[0].AttachmentId = 1054
        Features.Mailbox.PageMessages[0].Attachments[0].Ordinal = 0
        Features.Mailbox.PageMessages[0].Attachments[0].ItemType = 1055
        Features.Mailbox.PageMessages[0].Attachments[0].SourceListType = 1056
        Features.Mailbox.PageMessages[0].Attachments[0].SourceSlotIndex = 1057
        Features.Mailbox.PageMessages[0].Attachments[0].SourceItemUid = 1058
        Features.Mailbox.PageMessages[0].Attachments[0].ItemTemplateId = 1059
        Features.Mailbox.PageMessages[0].Attachments[0].ItemKind = "equipment"
        Features.Mailbox.PageMessages[0].Attachments[0].ItemCount = 1060
        Features.Mailbox.PageMessages[0].Attachments[0].InstanceValue = 1061
        Features.Mailbox.PageMessages[0].Attachments[0].Durability = 1062
        Features.Mailbox.PageMessages[0].Attachments[0].SealFlag = 1063
        Features.Mailbox.PageMessages[0].Attachments[0].OptionValue = 1064
        Features.Mailbox.PageMessages[0].Attachments[0].ExpireTime = 1065
        Features.Mailbox.PageMessages[0].Attachments[0].Marker16 = 1066
        Features.Mailbox.PageMessages[0].Attachments[0].PetSerialOrHandle = 1067
        Features.Mailbox.PageMessages[0].Attachments[0].ItemCore = 0x00E90300001100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000
        Features.Mailbox.PageMessages[0].Attachments[0].DetailJson = "detail"
        Features.Mailbox.PageMessages[0].GoldAttachments[0].AttachmentId = 1055
        Features.Mailbox.PageMessages[0].GoldAttachments[0].Amount = 123
        Features.PvpMissions[0].Item1 = 0
        Features.PvpMissions[0].Item2 = 900
        Features.PvpMissions[0].Item3 = 12
        """;
}
