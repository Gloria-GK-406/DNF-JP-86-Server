using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Characters;
using Dfo.Server.Persistence.Characters;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

public sealed class SqliteEntryLifecycleRecoveryTests
{
    [Fact]
    public async Task PvpProfile_DistinguishesMissingFromInitializedAndLoadsBothPages()
    {
        var path = TempPath();
        try
        {
            await ExecuteAsync(path, """
                CREATE TABLE characters(character_id INTEGER PRIMARY KEY,account_id INTEGER NOT NULL,delete_flag INTEGER NOT NULL DEFAULT 0);
                INSERT INTO characters VALUES(7,3,0);
            """);
            var repository = new SqliteCharacterPvpSkillProfileRepository(path);
            var empty = new CharacterSkillsEntryState(
                [new CharacterSkillPageState(0, []), new CharacterSkillPageState(1, [])], [],
                new CharacterSkillPointBasisState(0, 0), [], null);
            var initializedEmpty = await repository.LoadOrInitializeAsync(
                new AccountId(3), new CharacterId(7), empty, CancellationToken.None);
            Assert.Empty(initializedEmpty.Pages[0].Skills);
            await ExecuteAsync(path, """
                INSERT INTO character_pvp_skills(character_id,page_index,slot,skill_id,level,extra_values)
                VALUES(7,0,4,100,2,X'0102'),(7,1,9,200,3,NULL);
                """);

            var profile = Assert.IsType<CharacterSkillsEntryState>(await repository.LoadOrInitializeAsync(
                new AccountId(3), new CharacterId(7), empty, CancellationToken.None));
            Assert.Equal([100], profile.Pages[0].Skills.Select(value => (int)value.SkillId));
            Assert.Equal([200], profile.Pages[1].Skills.Select(value => (int)value.SkillId));
            Assert.Equal(ushort.MaxValue, profile.ProtocolMirror!.Page0Sp);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task DungeonLuckyStarRecovery_CommitsMutationAndOutboxAtomically()
    {
        var path = TempPath();
        try
        {
            await ExecuteAsync(path, """
                CREATE TABLE accounts(account_id INTEGER PRIMARY KEY,lucky_star INTEGER NOT NULL DEFAULT 0);
                INSERT INTO accounts VALUES(3,4);
                CREATE TABLE dungeon_persistent_effect_outbox(
                  source_event_id TEXT,effect_kind TEXT,effect_scope INTEGER,scope_target INTEGER,
                  character_id INTEGER,account_id INTEGER,payload_version INTEGER,payload_json TEXT,
                  state INTEGER,lease_id TEXT,lease_owner TEXT,lease_expires_at INTEGER DEFAULT 0,
                  attempt_count INTEGER DEFAULT 0,last_error TEXT DEFAULT '',result_version INTEGER,
                  result_json TEXT,created_at INTEGER,updated_at INTEGER,committed_at INTEGER,
                  PRIMARY KEY(source_event_id,effect_kind,effect_scope,scope_target));
                INSERT INTO dungeon_persistent_effect_outbox
                (source_event_id,effect_kind,effect_scope,scope_target,character_id,account_id,payload_version,
                 payload_json,state,created_at,updated_at)
                VALUES('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','suitable-dungeon-lucky-star',3,7,7,3,1,
                 '{"characterId":7,"accountId":3,"dungeonId":1,"clearLevel":2,"amount":1}',0,1,1);
                """);
            var repository = new SqliteDungeonPersistentEffectRecoveryRepository(path, new ProgressionPolicy(), new DungeonEffectRuleService());
            var outcome = await repository.RecoverAsync(
                new AccountId(3), new CharacterId(7), CancellationToken.None);

            Assert.Equal(1, outcome.CommittedCount);
            Assert.Equal(5L, await ScalarAsync(path, "SELECT lucky_star FROM accounts WHERE account_id=3"));
            Assert.Equal(2L, await ScalarAsync(path, "SELECT state FROM dungeon_persistent_effect_outbox"));
            Assert.Equal("{\"granted\":true,\"newTotal\":5}", await ScalarStringAsync(path,
                "SELECT result_json FROM dungeon_persistent_effect_outbox"));
            var retry = await repository.RecoverAsync(new AccountId(3), new CharacterId(7), CancellationToken.None);
            Assert.Equal(0, retry.CommittedCount);
            Assert.Equal(5L, await ScalarAsync(path, "SELECT lucky_star FROM accounts WHERE account_id=3"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task DungeonLuckyStarRecovery_AtCapCommitsGrantedFalseWithoutChangingWallet()
    {
        var path = TempPath();
        try
        {
            await SeedLuckyStarOutboxAsync(path, luckyStar: 999, payloadJson:
                "{\"characterId\":7,\"accountId\":3,\"dungeonId\":1,\"clearLevel\":2,\"amount\":1}");
            var outcome = await new SqliteDungeonPersistentEffectRecoveryRepository(path, new ProgressionPolicy(), new DungeonEffectRuleService())
                .RecoverAsync(new AccountId(3), new CharacterId(7), CancellationToken.None);

            Assert.Equal(1, outcome.CommittedCount);
            Assert.Equal(999L, await ScalarAsync(path, "SELECT lucky_star FROM accounts WHERE account_id=3"));
            Assert.Equal("{\"granted\":false,\"newTotal\":999}", await ScalarStringAsync(path,
                "SELECT result_json FROM dungeon_persistent_effect_outbox"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task DungeonRecovery_OverlappingMalformedRecordDeadLettersOnceWithoutStealingClaim()
    {
        var path = TempPath();
        try
        {
            await SeedLuckyStarOutboxAsync(path, luckyStar: 4, payloadJson: "{");
            var first = new SqliteDungeonPersistentEffectRecoveryRepository(path, new ProgressionPolicy(), new DungeonEffectRuleService());
            var second = new SqliteDungeonPersistentEffectRecoveryRepository(path, new ProgressionPolicy(), new DungeonEffectRuleService());

            var outcomes = await Task.WhenAll(
                first.RecoverAsync(new AccountId(3), new CharacterId(7), CancellationToken.None).AsTask(),
                second.RecoverAsync(new AccountId(3), new CharacterId(7), CancellationToken.None).AsTask());

            Assert.Equal(1, outcomes.Sum(value => value.DeadLetterCount));
            Assert.Equal(4L, await ScalarAsync(path, "SELECT state FROM dungeon_persistent_effect_outbox"));
            Assert.Equal(4L, await ScalarAsync(path, "SELECT lucky_star FROM accounts WHERE account_id=3"));
            Assert.Equal(1L, await ScalarAsync(path,
                "SELECT attempt_count FROM dungeon_persistent_effect_outbox"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task DungeonRecovery_WrongPlayerScopeOrTargetDeadLettersWithoutApplyingWalletMutation()
    {
        var path = TempPath();
        try
        {
            await ExecuteAsync(path, """
                CREATE TABLE accounts(account_id INTEGER PRIMARY KEY,lucky_star INTEGER NOT NULL DEFAULT 0);
                INSERT INTO accounts VALUES(3,4);
                CREATE TABLE dungeon_persistent_effect_outbox(
                  source_event_id TEXT,effect_kind TEXT,effect_scope INTEGER,scope_target INTEGER,
                  character_id INTEGER,account_id INTEGER,payload_version INTEGER,payload_json TEXT,
                  state INTEGER,lease_id TEXT,lease_owner TEXT,lease_expires_at INTEGER DEFAULT 0,
                  attempt_count INTEGER DEFAULT 0,last_error TEXT DEFAULT '',result_version INTEGER,
                  result_json TEXT,created_at INTEGER,updated_at INTEGER,committed_at INTEGER,
                  PRIMARY KEY(source_event_id,effect_kind,effect_scope,scope_target));
                INSERT INTO dungeon_persistent_effect_outbox
                (source_event_id,effect_kind,effect_scope,scope_target,character_id,account_id,payload_version,
                 payload_json,state,created_at,updated_at)
                VALUES('scope','suitable-dungeon-lucky-star',1,7,7,3,1,
                  '{"characterId":7,"accountId":3,"dungeonId":1,"clearLevel":2,"amount":1}',0,1,1),
                 ('target','suitable-dungeon-lucky-star',3,8,7,3,1,
                  '{"characterId":7,"accountId":3,"dungeonId":1,"clearLevel":2,"amount":1}',0,2,2);
                """);

            var outcome = await new SqliteDungeonPersistentEffectRecoveryRepository(path, new ProgressionPolicy(), new DungeonEffectRuleService())
                .RecoverAsync(new AccountId(3), new CharacterId(7), CancellationToken.None);

            Assert.Equal(2, outcome.DeadLetterCount);
            Assert.Equal(0, outcome.CommittedCount);
            Assert.Equal(4L, await ScalarAsync(path, "SELECT lucky_star FROM accounts WHERE account_id=3"));
            Assert.Equal(2L, await ScalarAsync(path,
                "SELECT COUNT(*) FROM dungeon_persistent_effect_outbox WHERE state=4"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task ExpertContractReconciliation_ActiveNoopsExpiredCapsAndWrongOwnerRollsBack()
    {
        var path = TempPath();
        try
        {
            var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            await ExecuteAsync(path, $"""
                CREATE TABLE characters(character_id INTEGER PRIMARY KEY,account_id INTEGER,delete_flag INTEGER DEFAULT 0);
                CREATE TABLE account_premiums(account_id INTEGER,premium_type INTEGER,end_time INTEGER,
                  PRIMARY KEY(account_id,premium_type));
                CREATE TABLE character_skills(character_id INTEGER,page_index INTEGER,slot INTEGER,
                  skill_id INTEGER,level INTEGER,extra_values BLOB,PRIMARY KEY(character_id,page_index,slot));
                INSERT INTO characters VALUES(7,3,0);
                INSERT INTO account_premiums VALUES(3,580,{future});
                INSERT INTO character_skills VALUES(7,0,1,100,4,NULL),
                  (7,0,2,101,1,NULL),(7,0,3,102,5,NULL),(7,0,4,103,3,NULL);
                """);
            var repository = new SqliteExpertSkillContractReconciliationRepository(path, new ExpertSkillContractRuleService());
            var definitions = new Dictionary<int, SkillPointCostDefinition>
            {
                [100] = new(100, false, [10], false, 1, 10, 0, 1, 1, OrdinaryMaximumLevel: 2),
                [101] = new(101, false, [10], false, 10, 10, 0, 1, 1, OrdinaryMaximumLevel: 0),
                [102] = new(102, false, [10], false, 1, 10, 0, 1, 1, OrdinaryMaximumLevel: null),
                [103] = new(103, false, [10], false, 10, 10, 0, 1, 1, OrdinaryMaximumLevel: 0),
            };
            Assert.False(await repository.ReconcileAsync(new AccountId(3), new CharacterId(7),
                new HashSet<int> { 580 }, definitions, new Dictionary<int, byte>(), CancellationToken.None));
            Assert.Equal(4L, await ScalarAsync(path, "SELECT level FROM character_skills"));

            await ExecuteAsync(path, "UPDATE account_premiums SET end_time=1");
            Assert.True(await repository.ReconcileAsync(new AccountId(3), new CharacterId(7),
                new HashSet<int> { 580 }, definitions, new Dictionary<int, byte> { [103] = 1 }, CancellationToken.None));
            Assert.Equal(2L, await ScalarAsync(path, "SELECT level FROM character_skills"));
            Assert.Equal(0L, await ScalarAsync(path, "SELECT COUNT(*) FROM character_skills WHERE skill_id=101"));
            Assert.Equal(5L, await ScalarAsync(path, "SELECT level FROM character_skills WHERE skill_id=102"));
            Assert.Equal(1L, await ScalarAsync(path, "SELECT level FROM character_skills WHERE skill_id=103"));
            await ExecuteAsync(path, "UPDATE characters SET account_id=4; UPDATE character_skills SET level=4");
            await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.ReconcileAsync(
                new AccountId(3), new CharacterId(7), new HashSet<int> { 580 }, definitions,
                new Dictionary<int, byte>(), CancellationToken.None));
            Assert.Equal(4L, await ScalarAsync(path, "SELECT level FROM character_skills"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task DungeonRecovery_ReclaimsExpiredExperienceAndDeadLettersBadRecordIndependently()
    {
        var path = TempPath();
        try
        {
            await ExecuteAsync(path, """
                CREATE TABLE accounts(account_id INTEGER PRIMARY KEY,lucky_star INTEGER NOT NULL DEFAULT 0,
                  honor_exp INTEGER NOT NULL DEFAULT 0,growth_capsule_exp INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE characters(character_id INTEGER PRIMARY KEY,account_id INTEGER NOT NULL,
                  level INTEGER NOT NULL,exp INTEGER NOT NULL,delete_flag INTEGER NOT NULL DEFAULT 0);
                INSERT INTO accounts VALUES(3,0,0,0); INSERT INTO characters VALUES(7,3,85,100,0);
                CREATE TABLE dungeon_persistent_effect_outbox(
                  source_event_id TEXT,effect_kind TEXT,effect_scope INTEGER,scope_target INTEGER,
                  character_id INTEGER,account_id INTEGER,payload_version INTEGER,payload_json TEXT,
                  state INTEGER,lease_id TEXT,lease_owner TEXT,lease_expires_at INTEGER DEFAULT 0,
                  attempt_count INTEGER DEFAULT 0,last_error TEXT DEFAULT '',result_version INTEGER,
                  result_json TEXT,created_at INTEGER,updated_at INTEGER,committed_at INTEGER,
                  PRIMARY KEY(source_event_id,effect_kind,effect_scope,scope_target));
                INSERT INTO dungeon_persistent_effect_outbox
                (source_event_id,effect_kind,effect_scope,scope_target,character_id,account_id,payload_version,
                 payload_json,state,lease_expires_at,created_at,updated_at)
                VALUES('bad','unknown',3,7,7,3,1,'{}',0,0,1,1),
                 ('xp','settlement-experience-grant',3,7,7,3,1,
                 '{"characterId":7,"accountId":3,"previousLevel":85,"previousExp":100,"rawGain":25,"normalizeMaxLevelExp":true,"expectedDatabaseLevel":85,"expectedDatabaseExp":100}',1,0,2,2);
                """);
            var result = await new SqliteDungeonPersistentEffectRecoveryRepository(path, new ProgressionPolicy(), new DungeonEffectRuleService()).RecoverAsync(
                new AccountId(3), new CharacterId(7), CancellationToken.None);

            Assert.Equal(1, result.CommittedCount);
            Assert.Equal(1, result.DeadLetterCount);
            Assert.Equal(125L, await ScalarAsync(path, "SELECT exp FROM characters WHERE character_id=7"));
            Assert.Equal(4L, await ScalarAsync(path, "SELECT state FROM dungeon_persistent_effect_outbox WHERE source_event_id='bad'"));
            Assert.Equal(2L, await ScalarAsync(path, "SELECT state FROM dungeon_persistent_effect_outbox WHERE source_event_id='xp'"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task DungeonExperienceRecovery_UsesDurableSettlementBaselineAndRetainsInDungeonDelta()
    {
        var path = TempPath();
        try
        {
            await ExecuteAsync(path, """
                CREATE TABLE accounts(account_id INTEGER PRIMARY KEY,lucky_star INTEGER NOT NULL DEFAULT 0,
                  honor_exp INTEGER NOT NULL DEFAULT 0,growth_capsule_exp INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE characters(character_id INTEGER PRIMARY KEY,account_id INTEGER NOT NULL,
                  level INTEGER NOT NULL,exp INTEGER NOT NULL,delete_flag INTEGER NOT NULL DEFAULT 0);
                INSERT INTO accounts VALUES(3,0,5,2); INSERT INTO characters VALUES(7,3,85,84000,0);
                CREATE TABLE dungeon_persistent_effect_outbox(
                  source_event_id TEXT,effect_kind TEXT,effect_scope INTEGER,scope_target INTEGER,
                  character_id INTEGER,account_id INTEGER,payload_version INTEGER,payload_json TEXT,
                  state INTEGER,lease_id TEXT,lease_owner TEXT,lease_expires_at INTEGER DEFAULT 0,
                  attempt_count INTEGER DEFAULT 0,last_error TEXT DEFAULT '',result_version INTEGER,
                  result_json TEXT,created_at INTEGER,updated_at INTEGER,committed_at INTEGER,
                  PRIMARY KEY(source_event_id,effect_kind,effect_scope,scope_target));
                INSERT INTO dungeon_persistent_effect_outbox
                (source_event_id,effect_kind,effect_scope,scope_target,character_id,account_id,payload_version,
                 payload_json,state,created_at,updated_at)
                VALUES('xp-delta','settlement-experience-grant',3,7,7,3,1,
                 '{"characterId":7,"accountId":3,"previousLevel":85,"previousExp":84990,"rawGain":25,"normalizeMaxLevelExp":true,"expectedDatabaseLevel":85,"expectedDatabaseExp":84000}',0,1,1);
                """);

            var outcome = await new SqliteDungeonPersistentEffectRecoveryRepository(path, new ProgressionPolicy(), new DungeonEffectRuleService())
                .RecoverAsync(new AccountId(3), new CharacterId(7), CancellationToken.None);

            Assert.Equal(1, outcome.CommittedCount);
            Assert.Equal(86L, await ScalarAsync(path, "SELECT level FROM characters WHERE character_id=7"));
            Assert.Equal(85000L, await ScalarAsync(path, "SELECT exp FROM characters WHERE character_id=7"));
            Assert.Equal(20L, await ScalarAsync(path, "SELECT honor_exp FROM accounts WHERE account_id=3"));
            Assert.Equal(17L, await ScalarAsync(path, "SELECT growth_capsule_exp FROM accounts WHERE account_id=3"));
            Assert.Equal("{\"rawGain\":25,\"honorExpGain\":15,\"normalExpGain\":10,\"previousLevel\":85,\"previousExp\":84990,\"newLevel\":86,\"newExp\":85000,\"normalizedMaxLevelExp\":false,\"persisted\":true,\"growthCapsuleExpGain\":15,\"totalHonorExp\":20,\"totalGrowthCapsuleExp\":17}",
                await ScalarStringAsync(path, "SELECT result_json FROM dungeon_persistent_effect_outbox"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    /// <summary>
    /// At and above the last level, the whole gain becomes honour rather than experience.
    /// </summary>
    /// <remarks>
    /// Pinned before the experience rule is moved out of persistence. The case above crosses into
    /// the last level and so exercises the other side of the same branch; nothing exercised this
    /// one, which meant the boundary itself -- at the level rather than past it -- was free to
    /// move without any test noticing.
    /// </remarks>
    [Fact]
    public async Task DungeonExperienceRecovery_AtTheLastLevelTurnsTheWholeGainIntoHonour()
    {
        var path = TempPath();
        try
        {
            await SeedExperienceOutboxAsync(path, level: 86, exp: 84000,
                """{"characterId":7,"accountId":3,"previousLevel":86,"previousExp":84000,"rawGain":25,"normalizeMaxLevelExp":true,"expectedDatabaseLevel":86,"expectedDatabaseExp":84000}""");

            var outcome = await new SqliteDungeonPersistentEffectRecoveryRepository(path, new ProgressionPolicy(), new DungeonEffectRuleService())
                .RecoverAsync(new AccountId(3), new CharacterId(7), CancellationToken.None);

            Assert.Equal(1, outcome.CommittedCount);
            Assert.Equal(86L, await ScalarAsync(path, "SELECT level FROM characters WHERE character_id=7"));
            Assert.Equal(85000L, await ScalarAsync(path, "SELECT exp FROM characters WHERE character_id=7"));
            Assert.Equal(30L, await ScalarAsync(path, "SELECT honor_exp FROM accounts WHERE account_id=3"));
            Assert.Equal(27L, await ScalarAsync(path, "SELECT growth_capsule_exp FROM accounts WHERE account_id=3"));
            Assert.Equal("{\"rawGain\":25,\"honorExpGain\":25,\"normalExpGain\":0,\"previousLevel\":86,\"previousExp\":84000,\"newLevel\":86,\"newExp\":85000,\"normalizedMaxLevelExp\":true,\"persisted\":true,\"growthCapsuleExpGain\":25,\"totalHonorExp\":30,\"totalGrowthCapsuleExp\":27}",
                await ScalarStringAsync(path, "SELECT result_json FROM dungeon_persistent_effect_outbox"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    /// <remarks>
    /// Below the last level and staying below it, the gain is ordinary experience and no honour is
    /// produced at all -- so the account is not touched. Pinned for the same reason as above.
    /// </remarks>
    [Fact]
    public async Task DungeonExperienceRecovery_BelowTheLastLevelProducesNoHonourAndLeavesTheAccountAlone()
    {
        var path = TempPath();
        try
        {
            await SeedExperienceOutboxAsync(path, level: 85, exp: 80000,
                """{"characterId":7,"accountId":3,"previousLevel":85,"previousExp":80000,"rawGain":25,"normalizeMaxLevelExp":false,"expectedDatabaseLevel":85,"expectedDatabaseExp":80000}""");

            var outcome = await new SqliteDungeonPersistentEffectRecoveryRepository(path, new ProgressionPolicy(), new DungeonEffectRuleService())
                .RecoverAsync(new AccountId(3), new CharacterId(7), CancellationToken.None);

            Assert.Equal(1, outcome.CommittedCount);
            Assert.Equal(85L, await ScalarAsync(path, "SELECT level FROM characters WHERE character_id=7"));
            Assert.Equal(80025L, await ScalarAsync(path, "SELECT exp FROM characters WHERE character_id=7"));
            Assert.Equal(5L, await ScalarAsync(path, "SELECT honor_exp FROM accounts WHERE account_id=3"));
            Assert.Equal(2L, await ScalarAsync(path, "SELECT growth_capsule_exp FROM accounts WHERE account_id=3"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");

    private static Task SeedExperienceOutboxAsync(string path, int level, long exp, string payloadJson) =>
        ExecuteAsync(path, $"""
            CREATE TABLE accounts(account_id INTEGER PRIMARY KEY,lucky_star INTEGER NOT NULL DEFAULT 0,
              honor_exp INTEGER NOT NULL DEFAULT 0,growth_capsule_exp INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE characters(character_id INTEGER PRIMARY KEY,account_id INTEGER NOT NULL,
              level INTEGER NOT NULL,exp INTEGER NOT NULL,delete_flag INTEGER NOT NULL DEFAULT 0);
            INSERT INTO accounts VALUES(3,0,5,2); INSERT INTO characters VALUES(7,3,{level},{exp},0);
            CREATE TABLE dungeon_persistent_effect_outbox(
              source_event_id TEXT,effect_kind TEXT,effect_scope INTEGER,scope_target INTEGER,
              character_id INTEGER,account_id INTEGER,payload_version INTEGER,payload_json TEXT,
              state INTEGER,lease_id TEXT,lease_owner TEXT,lease_expires_at INTEGER DEFAULT 0,
              attempt_count INTEGER DEFAULT 0,last_error TEXT DEFAULT '',result_version INTEGER,
              result_json TEXT,created_at INTEGER,updated_at INTEGER,committed_at INTEGER,
              PRIMARY KEY(source_event_id,effect_kind,effect_scope,scope_target));
            INSERT INTO dungeon_persistent_effect_outbox
            (source_event_id,effect_kind,effect_scope,scope_target,character_id,account_id,payload_version,
             payload_json,state,created_at,updated_at)
            VALUES('xp-delta','settlement-experience-grant',3,7,7,3,1,
             '{payloadJson.Replace("'", "''")}',0,1,1);
            """);

    private static Task SeedLuckyStarOutboxAsync(string path, int luckyStar, string payloadJson) =>
        ExecuteAsync(path, $"""
            CREATE TABLE accounts(account_id INTEGER PRIMARY KEY,lucky_star INTEGER NOT NULL DEFAULT 0);
            INSERT INTO accounts VALUES(3,{luckyStar});
            CREATE TABLE dungeon_persistent_effect_outbox(
              source_event_id TEXT,effect_kind TEXT,effect_scope INTEGER,scope_target INTEGER,
              character_id INTEGER,account_id INTEGER,payload_version INTEGER,payload_json TEXT,
              state INTEGER,lease_id TEXT,lease_owner TEXT,lease_expires_at INTEGER DEFAULT 0,
              attempt_count INTEGER DEFAULT 0,last_error TEXT DEFAULT '',result_version INTEGER,
              result_json TEXT,created_at INTEGER,updated_at INTEGER,committed_at INTEGER,
              PRIMARY KEY(source_event_id,effect_kind,effect_scope,scope_target));
            INSERT INTO dungeon_persistent_effect_outbox
            (source_event_id,effect_kind,effect_scope,scope_target,character_id,account_id,payload_version,
             payload_json,state,created_at,updated_at)
            VALUES('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','suitable-dungeon-lucky-star',3,7,7,3,1,
             '{payloadJson.Replace("'", "''")}',0,1,1);
            """);
    private sealed class ProgressionPolicy : IDungeonExperienceProgressionPolicySource
    {
        public DungeonExperienceProgressionPolicy Load() => new(
            Enumerable.Range(1, 85).Select(value => checked((uint)(value * 1000))).ToArray(),
            1_000_000, 10_000, 100);
    }
    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<long> ScalarAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
    private static async Task<string> ScalarStringAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }
}
