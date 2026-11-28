using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

public sealed class SchemaVersionTests
{
    [Fact]
    public void Version_forty_nine_adds_timed_effects_and_preserves_legacy_effect_magnitudes()
    {
        var path = Unversioned.NewPath("timed-item-effects-v49");
        try
        {
            _ = Unversioned.Open(path);
            var freshShape = ShapeOf(path);
            Execute(path, """
                DROP TABLE character_timed_item_effects;
                PRAGMA user_version=49;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name) VALUES(71,7,'member');
                INSERT INTO character_item_effects(character_id,sort_order,item_id,value) VALUES(71,0,10004687,180);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((49L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(freshShape, ShapeOf(path));
            Assert.Equal(180, Scalar(path, "SELECT value FROM character_item_effects WHERE character_id=71 AND item_id=10004687"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM character_timed_item_effects"));
            Assert.Null(Migrator.Migrate(path).Backup);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    public void Guild_branch_recovers_its_position_after_an_interrupted_pvp_upgrade(int guildVersion)
    {
        var path = Unversioned.NewPath($"guild-branch-v{guildVersion}");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, $"""
                INSERT INTO accounts(account_id,m_id) VALUES(7,'keep');
                INSERT INTO characters(character_id,account_id,name) VALUES(71,7,'member');
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at,gm_points) VALUES(3,'kept','KEPT','',1,9);
                INSERT INTO guild_members(membership_id,character_id,guild_id,is_leader,joined_at) VALUES(8301,71,3,1,7);
                INSERT INTO guild_gold_donations(guild_id,donor_character_id,donor_name,gold,points,donated_at)
                    VALUES(3,71,X'61',1000,1,7);
                CREATE TABLE character_pvp(character_id INTEGER PRIMARY KEY,pvp_grade INTEGER,pvp_rating_grade INTEGER,
                    skills_initialized INTEGER,pvp_rank_point INTEGER,pvp_stat_a INTEGER,pvp_win_streak INTEGER,pvp_lose_streak INTEGER);
                INSERT INTO character_pvp VALUES(71,5,4,1,0,88,0,0);
                DROP TABLE pvp_colosseum_runs;
                CREATE VIEW pvp_colosseum_runs AS SELECT 1;
                PRAGMA user_version={guildVersion};
                """);

            // Simulate a failure in the last PvP step, after the legacy character_pvp table is gone.
            Assert.Throws<SqliteException>(() => Migrator.Migrate(path, backup: false));
            Assert.Equal(20L, Scalar(path, "PRAGMA user_version"));
            Assert.Equal(0L, Scalar(path, "SELECT COUNT(*) FROM sqlite_schema WHERE name='character_pvp'"));
            Assert.Equal(guildVersion, Scalar(path, "SELECT guild_version FROM migration_guild_branch_resume"));
            Execute(path, "DROP VIEW pvp_colosseum_runs");

            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((20L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(5L, Scalar(path, "SELECT grade FROM character_pvp_record WHERE character_id=71 AND rating_grade=4"));
            Assert.Equal(88L, Scalar(path, "SELECT stat_a FROM character_pvp_legacy WHERE character_id=71"));
            Assert.Equal(9L, Scalar(path, "SELECT gm_points FROM guilds WHERE guild_id=3"));
            // Old v20 still needs its one-time ledger backfill; old v40 has already frozen attribution.
            Assert.Equal(guildVersion == 40 ? 0L : 8301L, Scalar(path,
                "SELECT COALESCE(membership_id,0) FROM guild_gold_donations WHERE donor_character_id=71"));
            Assert.Equal(0L, Scalar(path, "SELECT COUNT(*) FROM sqlite_schema WHERE name='migration_guild_branch_resume'"));
            Assert.Null(Migrator.Migrate(path).Backup);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void Version_twenty_adds_colosseum_state_without_rewriting_existing_accounts()
    {
        var path = Unversioned.NewPath("pvp-colosseum-v20");
        try
        {
            _ = Unversioned.Open(path);
            var freshShape = ShapeOf(path);
            Execute(path, """
                DROP TABLE pvp_colosseum_runs;
                PRAGMA user_version=20;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((20L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(freshShape, ShapeOf(path));
            Assert.Equal(1, Scalar(path, "SELECT COUNT(*) FROM accounts WHERE account_id=7"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM pvp_colosseum_runs"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_eighteen_adds_daily_missions_and_keeps_existing_gp()
    {
        var path = Unversioned.NewPath("pvp-mission-v18");
        try
        {
            _ = Unversioned.Open(path);
            var freshShape = ShapeOf(path);
            Execute(path, """
                DROP TABLE pvp_daily_mission_metrics;
                DROP TABLE pvp_daily_missions;
                PRAGMA user_version=18;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO pvp_matches(match_id,room_id,listener_port,channel_env,battle_mode,outcome_kind,winner,
                    records_result,game_day,completed_at)
                VALUES('old-total-match',1,44081,8,8,0,0,1,'2026-11-25',1795564800000);
                INSERT INTO account_pvp_total_match_rating_results
                    (match_id,account_id,season_id,placement_games,gp_before,gp_after,peak_gp)
                VALUES('old-total-match',7,1,1,1000,1020,0);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((18L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(freshShape, ShapeOf(path));
            Assert.Equal(1020, Scalar(path, "SELECT gp_after FROM account_pvp_total_match_rating_results WHERE account_id=7"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM pvp_daily_missions"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_sixteen_upgrade_preserves_old_match_results_and_starts_an_empty_rating_journal()
    {
        var path = Unversioned.NewPath("pvp-rating-v16");
        try
        {
            _ = Unversioned.Open(path);
            var freshShape = ShapeOf(path);
            Execute(path, """
                DROP TABLE IF EXISTS pvp_match_rating_results;
                PRAGMA user_version=16;
                INSERT INTO pvp_matches(match_id,room_id,listener_port,channel_env,battle_mode,outcome_kind,winner,
                    records_result,game_day,completed_at)
                VALUES('old-match',1,44060,8,1,0,0,0,'2026-11-25',1795564800000);
                INSERT INTO pvp_match_results(match_id,character_id,seat,team,outcome,kills,deaths,fled,
                    counts_toward_daily,previous_grade,grade,experience_change,experience,win_streak)
                VALUES('old-match',71,0,0,0,1,0,0,0,7,7,0,1250,0);
                """);

            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((16L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(freshShape, ShapeOf(path));
            Assert.Equal(1, Scalar(path, "SELECT COUNT(*) FROM pvp_match_results WHERE match_id='old-match' AND experience=1250"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM pvp_match_rating_results"));

            const string insert = """
                PRAGMA foreign_keys=ON;
                INSERT INTO pvp_match_rating_results(match_id,character_id,season_id,placement_games,rp_before,rp_after,peak_rp)
                """;
            Assert.Throws<SqliteException>(() => Execute(path, insert + " VALUES('old-match',72,1,1,1000,1020,0);"));
            Assert.Throws<SqliteException>(() => Execute(path, insert + " VALUES('old-match',71,1,21,1000,1020,0);"));
            Execute(path, insert + " VALUES('old-match',71,1,1,1000,1020,0);");
            Assert.Throws<SqliteException>(() => Execute(path, insert + " VALUES('old-match',71,1,2,1020,1040,0);"));
            Assert.Equal(1020, Scalar(path, "SELECT rp_after FROM pvp_match_rating_results WHERE match_id='old-match' AND character_id=71"));
            Execute(path, "PRAGMA foreign_keys=ON; DELETE FROM pvp_match_results WHERE match_id='old-match';");
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM pvp_match_rating_results"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_seventeen_upgrade_adds_empty_account_gp_journal()
    {
        var path = Unversioned.NewPath("pvp-total-match-rating-v17");
        try
        {
            _ = Unversioned.Open(path);
            var freshShape = ShapeOf(path);
            Execute(path, """
                DROP TABLE account_pvp_total_match_rating_results;
                PRAGMA user_version=17;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO account_pvp_total_match_teams
                    (account_id,name,name_key,first_character_id,second_character_id,third_character_id)
                VALUES(7,'team','TEAM',71,72,73);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((17L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(freshShape, ShapeOf(path));
            Assert.Equal(1, Scalar(path, "SELECT COUNT(*) FROM account_pvp_total_match_teams WHERE account_id=7"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM account_pvp_total_match_rating_results"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_fifteen_upgrade_adds_empty_total_match_teams_and_preserves_the_roster()
    {
        var path = Unversioned.NewPath("pvp-total-match-v15");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                DROP TABLE account_pvp_total_match_teams;
                PRAGMA user_version=15;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,slot_index) VALUES
                    (71,7,'one',1,0,70,0,0),(72,7,'two',2,0,70,0,2),(73,7,'three',3,0,70,0,4);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal(15L, migrated.From);
            Assert.Equal(CharacterDatabaseSchema.Version, migrated.To);
            Assert.Equal(3, Scalar(path, "SELECT COUNT(*) FROM characters WHERE account_id=7"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM account_pvp_total_match_teams"));
            _ = Unversioned.Open(path);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_47_freezes_existing_membership_attribution_and_preserves_departure_behavior()
    {
        var path = Unversioned.NewPath("guild-identity-v47");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                DROP VIEW guild_contribution_entries;
                DROP TABLE guild_members;
                CREATE TABLE guild_members (
                    character_id INTEGER PRIMARY KEY REFERENCES characters(character_id),
                    guild_id INTEGER NOT NULL REFERENCES guilds(guild_id) ON DELETE CASCADE,
                    is_leader INTEGER NOT NULL, joined_at INTEGER NOT NULL, donation_points INTEGER NOT NULL,
                    member_rank INTEGER NOT NULL, memo TEXT NOT NULL);
                ALTER TABLE guild_gold_donations DROP COLUMN membership_id;
                ALTER TABLE guild_monthly_contribution_events DROP COLUMN membership_id;
                ALTER TABLE guild_daily_contributions DROP COLUMN membership_id;
                ALTER TABLE guild_clear_contributions DROP COLUMN membership_id;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'keep');
                INSERT INTO characters(character_id,account_id,name) VALUES(71,7,'member');
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at) VALUES(3,'guild','GUILD','',1);
                INSERT INTO guild_members VALUES(71,3,1,100,5,4,'original memo');
                INSERT INTO guild_gold_donations(guild_id,donor_character_id,donor_name,gold,points,donated_at)
                    VALUES(3,71,X'61',1000,1,99),(3,71,X'61',2000,2,100),(3,71,X'61',3000,3,101);
                INSERT INTO guild_monthly_contribution_events(guild_id,character_id,character_name,source,event_key,earned_at,points)
                    VALUES(3,71,X'61',2,'day',101,10);
                INSERT INTO guild_daily_contributions(guild_id,character_id,kind,day,earned_at,points) VALUES(3,71,1,1,100,3);
                INSERT INTO guild_clear_contributions(guild_id,character_id,run_id,kind,day,earned_at,points)
                    VALUES(3,71,'run',2,1,99,4);
                INSERT INTO guild_red_packets(guild_id,sender_id,sender_account_id,sender_name,item_id,total_amount,
                    contribution_rate,shares,remaining,shares_left,issued_at,expires_at)
                    VALUES(3,71,7,'member',1,100,50,5,100,5,100,200);
                PRAGMA user_version=47;
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((47L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            var id = Scalar(path, "SELECT membership_id FROM guild_members WHERE character_id=71 AND is_leader=1 AND memo='original memo'");
            Assert.True(id > 0);
            Assert.Equal(16, Scalar(path, "SELECT SUM(points) FROM guild_contribution_entries"));
            Assert.Equal(15, Scalar(path, $"SELECT SUM(points) FROM guild_contribution_entries WHERE membership_id={id}"));
            Assert.Equal(id, Scalar(path, "SELECT membership_id FROM guild_daily_contributions"));
            Assert.Equal(1, Scalar(path, "SELECT COUNT(*) FROM guild_clear_contributions WHERE membership_id IS NULL"));
            Assert.Equal(0, Scalar(path, "SELECT sender_departed FROM guild_red_packets"));
            Execute(path, """
                DELETE FROM guild_members WHERE character_id=71;
                INSERT INTO guild_members(character_id,guild_id,is_leader,joined_at) VALUES(71,3,1,100);
                """);
            Assert.NotEqual(id, Scalar(path, "SELECT membership_id FROM guild_members WHERE character_id=71"));
            Assert.Equal(1, Scalar(path, "SELECT sender_departed FROM guild_red_packets"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM guild_contribution_entries JOIN guild_members USING(membership_id)"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
            Assert.Equal(migrated.To, Migrator.Migrate(path, backup: false).From);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_46_adds_unset_medal_aura_without_changing_the_guild()
    {
        var path = Unversioned.NewPath("guild-medal-aura-v46");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                ALTER TABLE guilds DROP COLUMN medal_aura_frame;
                ALTER TABLE guilds DROP COLUMN medal_aura_symbol;
                PRAGMA user_version=46;
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at,gm_points)
                    VALUES(3,'kept','kept','',1800000000,42);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((46L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(-1, Scalar(path, "SELECT medal_aura_symbol FROM guilds WHERE guild_id=3"));
            Assert.Equal(-1, Scalar(path, "SELECT medal_aura_frame FROM guilds WHERE guild_id=3"));
            Assert.Equal(42, Scalar(path, "SELECT gm_points FROM guilds WHERE guild_id=3"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_45_adds_empty_extensions_and_keeps_dungeon_progress()
    {
        var path = Unversioned.NewPath("guild-extension-v45");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                DROP TABLE guild_dungeon_extensions;
                PRAGMA user_version=45;
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at) VALUES(3,'kept','kept','',1800000000);
                INSERT INTO guild_dungeon_progress(guild_id,period_start,style_id,dungeon_id,clear_count)
                    VALUES(3,1800000000,0,8500,100);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((45L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(100, Scalar(path, "SELECT clear_count FROM guild_dungeon_progress WHERE guild_id=3"));
            Assert.Equal(0, Scalar(path, "SELECT count(*) FROM guild_dungeon_extensions"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_43_preserves_dated_donations_in_the_contribution_view()
    {
        var path = Unversioned.NewPath("guild-month-v43");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                DROP VIEW guild_contribution_entries;
                DROP TABLE guild_monthly_contribution_rewards;
                DROP TABLE guild_monthly_contribution_events;
                PRAGMA user_version=43;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp)
                    VALUES(71,7,'kept',0,0,50,0);
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at) VALUES(3,'kept','kept','',1800000000);
                INSERT INTO guild_gold_donations(guild_id,donor_character_id,donor_name,gold,points,donated_at)
                    VALUES(3,71,X'6B657074',10000,10,1800000000);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((43L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(10, Scalar(path, "SELECT points FROM guild_contribution_entries WHERE source=3"));
            Assert.Equal(0, Scalar(path, "SELECT count(*) FROM guild_monthly_contribution_rewards"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_42_adds_repeat_bonus_without_mixing_it_with_old_clears()
    {
        var path = Unversioned.NewPath("guild-repeat-v42");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                DROP TABLE guild_dungeon_repeat_bonus;
                PRAGMA user_version=42;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp) VALUES(71,7,'kept',0,0,50,0);
                INSERT INTO guild_clear_contributions(character_id,run_id,kind,day,earned_at,points)
                VALUES(71,'earlier',1,739000,1800000000,10);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((42L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(10, Scalar(path, "SELECT points FROM guild_clear_contributions WHERE character_id=71"));
            Assert.Equal(0, Scalar(path, "SELECT count(*) FROM guild_dungeon_repeat_bonus"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_41_adds_clear_contributions_and_keeps_daily_rewards()
    {
        var path = Unversioned.NewPath("guild-clear-v41");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                DROP TABLE guild_clear_contributions;
                PRAGMA user_version=41;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp) VALUES(71,7,'kept',0,0,50,0);
                INSERT INTO guild_daily_contributions(character_id,kind,day,earned_at,points) VALUES(71,1,739000,1800000000,3);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((41L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(3, Scalar(path, "SELECT points FROM guild_daily_contributions WHERE character_id=71"));
            Assert.Equal(0, Scalar(path, "SELECT count(*) FROM guild_clear_contributions"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_40_adds_daily_contributions_and_keeps_red_packets()
    {
        var path = Unversioned.NewPath("guild-daily-v40");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                DROP TABLE guild_daily_contributions;
                PRAGMA user_version=40;
                INSERT INTO guild_red_packets(guild_id,sender_id,sender_account_id,sender_name,item_id,total_amount,
                    contribution_rate,shares,remaining,shares_left,issued_at,expires_at,settled_at,contribution)
                VALUES(1,71,7,'sender',490003266,5000,80,5,0,0,1,2,2,40);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal((40L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(40, Scalar(path, "SELECT contribution FROM guild_red_packets"));
            Assert.Equal(0, Scalar(path, "SELECT count(*) FROM guild_daily_contributions"));
            Assert.Equal(1, Scalar(path, "SELECT count(*) FROM sqlite_master WHERE name='ix_guild_daily_contributions_guild'"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_two_upgrade_adds_activity_state_without_changing_character_fatigue()
    {
        var path = Unversioned.NewPath("activity-v2");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                DROP TABLE event_activation_state;
                DROP TABLE IF EXISTS fatigue_consumption_settings;
                PRAGMA user_version=2;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp) VALUES(71,7,'kept',0,0,1,0);
                INSERT INTO character_fatigue(character_id,used,consumed_today,updated_at) VALUES(71,25,40,123456);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal(2L, migrated.From);
            Assert.Equal(CharacterDatabaseSchema.Version, migrated.To);
            Assert.Equal(25, Scalar(path, "SELECT used FROM character_fatigue WHERE character_id=71"));
            Assert.Equal(40, Scalar(path, "SELECT consumed_today FROM character_fatigue WHERE character_id=71"));
            Assert.Equal(123456, Scalar(path, "SELECT updated_at FROM character_fatigue WHERE character_id=71"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE name='fatigue_consumption_settings'"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM event_activation_state"));
            _ = Unversioned.Open(path);
            Assert.Null(Migrator.Migrate(path, backup: false).Backup);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_three_upgrade_opens_the_slots_of_characters_that_finished_649_or_650()
    {
        var path = Unversioned.NewPath("slot-expansion-v3");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                PRAGMA user_version=3;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,ex_equip_slot_stat) VALUES
                    (71,7,'none',0,0,70,0,0),(72,7,'support',0,0,70,0,0),(73,7,'stone',0,0,70,0,0),
                    (74,7,'both',0,0,70,0,0),(75,7,'kept',0,0,70,0,2),(76,7,'carried',0,0,70,0,0);
                INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES
                    (71,674,1),(72,649,1),(73,650,1),(74,649,1),(74,650,1),(75,649,1),(76,649,0);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal(3L, migrated.From);
            Assert.Equal(CharacterDatabaseSchema.Version, migrated.To);
            Assert.Equal(0, Scalar(path, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=71"));
            Assert.Equal(1, Scalar(path, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=72"));
            Assert.Equal(2, Scalar(path, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=73"));
            Assert.Equal(3, Scalar(path, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=74"));
            Assert.Equal(3, Scalar(path, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=75"));
            Assert.Equal(0, Scalar(path, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=76"));
            Assert.Equal(7, Scalar(path, "SELECT COUNT(*) FROM character_invisible_flags"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_four_upgrade_adds_character_relations_and_keeps_the_characters()
    {
        var path = Unversioned.NewPath("relations-v4");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                DROP TABLE character_relations;
                PRAGMA user_version=4;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp) VALUES
                    (71,7,'first',3,1,61,123),(72,7,'second',4,0,62,456);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal(4L, migrated.From);
            Assert.Equal(CharacterDatabaseSchema.Version, migrated.To);
            Assert.Equal(2, Scalar(path, "SELECT COUNT(*) FROM characters"));
            Assert.Equal(123, Scalar(path, "SELECT exp FROM characters WHERE character_id=71 AND name='first' AND level=61"));
            Assert.Equal(1, Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_character_relations_target'"));
            Execute(path, "INSERT INTO character_relations(owner_id,target_id,kind,created_at) VALUES(71,72,1,1800000000),(72,71,2,1800000001);");
            Assert.Equal(2, Scalar(path, "SELECT COUNT(*) FROM character_relations"));
            _ = Unversioned.Open(path);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_six_upgrade_adds_the_auction_tables_and_keeps_every_character()
    {
        var path = Unversioned.NewPath("auction-v6");
        var fresh = Unversioned.NewPath("auction-fresh");
        try
        {
            _ = Unversioned.Open(fresh);
            _ = Unversioned.Open(path);
            // A version 6 database has no auction tables yet.
            Execute(path, """
                DROP TABLE auction_trades;
                DROP TABLE auction_custody;
                DROP TABLE auction_listings;
                PRAGMA user_version=6;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,slot_index) VALUES
                    (71,7,'first',0,0,86,123,0);
                """);

            var migrated = Migrator.Migrate(path, backup: false);

            Assert.Equal((6L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(3, Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name LIKE 'auction_%'"));
            Assert.Equal(86, Scalar(path, "SELECT level FROM characters WHERE character_id=71"));
            Assert.Equal(ShapeOf(fresh), ShapeOf(path));
            _ = Unversioned.Open(path);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            File.Delete(fresh);
        }
    }

    [Fact]
    public void Version_five_upgrade_drops_manage_level_and_keeps_every_character()
    {
        var path = Unversioned.NewPath("adventure-group-v5");
        var fresh = Unversioned.NewPath("adventure-group-fresh");
        try
        {
            _ = Unversioned.Open(fresh);
            _ = Unversioned.Open(path);
            // A version 5 database still has the per-character column nothing ever wrote.
            Execute(path, """
                ALTER TABLE characters ADD COLUMN manage_level INTEGER NOT NULL DEFAULT 0;
                PRAGMA user_version=5;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,slot_index,manage_level) VALUES
                    (71,7,'first',0,0,86,123,0,0),(72,7,'second',1,0,70,45,1,3);
                """);

            var migrated = Migrator.Migrate(path, backup: false);

            Assert.Equal((5L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM pragma_table_info('characters') WHERE name='manage_level'"));
            Assert.Equal(2, Scalar(path, "SELECT COUNT(*) FROM characters WHERE account_id=7"));
            Assert.Equal(86, Scalar(path, "SELECT level FROM characters WHERE character_id=71"));
            Assert.Equal(45, Scalar(path, "SELECT exp FROM characters WHERE character_id=72"));
            Assert.Equal(ShapeOf(fresh), ShapeOf(path));
            _ = Unversioned.Open(path);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            File.Delete(fresh);
        }
    }

    /// <summary>
    /// An unversioned database with what the old server left in it comes out of the tool in
    /// exactly the shape the server builds a new one in.
    /// </summary>
    /// <remarks>
    /// The server only checks the version, so a migration that stamped the version without
    /// reaching the shape would be accepted and fail later on whatever statement met the
    /// difference. This is what holds the two to the same thing.
    /// </remarks>
    [Fact]
    public void A_migrated_database_has_exactly_the_shape_the_server_builds()
    {
        var fresh = Unversioned.NewPath("fresh");
        var migrated = Unversioned.NewPath("migrated");
        try
        {
            _ = Unversioned.Open(fresh);
            Unversioned.CreateUnstamped(migrated);
            Execute(migrated, """
                CREATE TABLE login_tickets(ticket TEXT PRIMARY KEY);
                CREATE TABLE schema_migrations(name TEXT PRIMARY KEY, applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
                CREATE TABLE marriage_housing_migrations(version INTEGER PRIMARY KEY, applied_at INTEGER NOT NULL);
                CREATE UNIQUE INDEX idx_item_slots_account_slot ON item_slots(owner_id,slot_index) WHERE owner_scope='account';
                DROP INDEX idx_mailbox_pending_equipment_uid;
                ALTER TABLE mailbox_attachments DROP COLUMN asset_kind;
                ALTER TABLE mailbox_messages ADD COLUMN gold INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE mailbox_recipients ADD COLUMN received_gold_flag INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE characters ADD COLUMN skill_tree_index INTEGER NOT NULL DEFAULT -1;
                ALTER TABLE characters ADD COLUMN appearance_blob BLOB;
                ALTER TABLE accounts ADD COLUMN cube_black INTEGER NOT NULL DEFAULT 0;
                """);

            Unversioned.Migrate(migrated);

            Assert.Equal(ShapeOf(fresh), ShapeOf(migrated));
            Assert.Equal(CharacterDatabaseSchema.Version, Scalar(migrated, "PRAGMA user_version"));
            _ = Unversioned.Open(migrated);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(fresh);
            File.Delete(migrated);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CharacterDatabaseSchema.Version + 1)]
    public void The_server_refuses_a_database_in_another_version(int version)
    {
        var path = Unversioned.NewPath("refused");
        try
        {
            Unversioned.CreateUnstamped(path);
            Execute(path, $"PRAGMA user_version={version};");

            var refusal = Assert.Throws<InvalidOperationException>(() => Unversioned.Open(path));
            Assert.Contains("DbMigrate", refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void The_database_is_backed_up_before_anything_changes_and_a_current_one_is_left_alone()
    {
        var path = Unversioned.NewPath("backup");
        string? backup = null;
        try
        {
            Unversioned.CreateUnstamped(path);
            Execute(path, "INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');");

            var first = Migrator.Migrate(path);
            backup = first.Backup;

            Assert.Equal((0L, (long)CharacterDatabaseSchema.Version), (first.From, first.To));
            Assert.True(File.Exists(backup));
            Assert.Equal(0L, Scalar(backup!, "PRAGMA user_version"));
            Assert.Equal(1L, Scalar(backup!, "SELECT COUNT(*) FROM accounts WHERE m_id='kept'"));

            var again = Migrator.Migrate(path);
            Assert.Null(again.Backup);
            Assert.Equal(again.From, again.To);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            if (backup is not null) File.Delete(backup);
        }
    }

    /// <summary>Every table's columns, every index and every trigger, in a fixed order.</summary>
    private static string[] ShapeOf(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var shape = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.name||'.'||c.name||':'||c.type||':'||c."notnull"||':'||IFNULL(c.dflt_value,'')||':'||c.pk
            FROM sqlite_master m JOIN pragma_table_info(m.name) c
            WHERE m.type='table' AND m.name NOT LIKE 'sqlite_%'
            UNION ALL
            SELECT m.type||' '||m.name||' on '||m.tbl_name||': '||IFNULL(m.sql,'')
            FROM sqlite_master m WHERE m.type IN ('index','trigger')
            ORDER BY 1;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read()) shape.Add(reader.GetString(0));
        return [.. shape];
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
