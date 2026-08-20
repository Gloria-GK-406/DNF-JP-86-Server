using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Accounts;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class SqliteAccountEntryStateRepositoryTests
{
    [Fact]
    public async Task AccountEntryState_LoadsPopulatedNormalizedCrossSliceStateLosslessly()
    {
        var path = CreateDatabase();
        try
        {
            var repository = new SqliteAccountEntryStateRepository(path);
            await ExecuteAsync(path, """
                INSERT INTO accounts(account_id,m_id,password_hash,cera,token_cera,happy_token_cera,
                    lucky_star,honor_exp,growth_capsule_exp)
                    VALUES(51,'account-51','',2147483647,32,64,65535,4294967297,4294967295);
                INSERT INTO account_settings(account_id,main_game_option,quickchat_bank0,quickchat_bank1,
                    hotkey_key_type,hotkey_slots) VALUES(51,X'01020304',X'05',X'0607',1,X'34127856');
                INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(51,2,5000000000),(51,586,6000000000);
                INSERT INTO characters(character_id,account_id,name,level,slot_index,delete_flag)
                    VALUES(501,51,'one',60,0,0),(502,51,'two',40,1,0);
                INSERT INTO account_increase_chance_lottery_progress(account_id,item_template_id,reward_index)
                    VALUES(51,1001,3),(51,1001,7),(51,1002,1);
                INSERT INTO character_mercenary_support(owner_character_id,slot,support_character_id,skill_id,striker_skill_id)
                    VALUES(501,2,502,7001,7002);
                INSERT INTO character_buy_restrict_items(character_id,sort_order,entry_id,sentinel_or_value,flag)
                    VALUES(501,0,8001,-1,1),(501,1,8002,2147483647,0),
                          (502,0,8001,-1,1),(502,1,8002,2147483647,0);
                INSERT INTO character_init_flags(character_id,pc_room_state) VALUES(501,7),(502,7);
                """);

            var result = await repository.LoadAsync(new AccountId(51), CancellationToken.None);

            Assert.Equal([1, 2, 3, 4], result.Settings.MainGameOptions);
            Assert.Equal([0x34, 0x12], result.Settings.AccountHotkeySlotPrefix);
            Assert.Equal(2_147_483_647L, result.WalletEntitlements.Cera);
            Assert.Equal(5_000_000_000L, result.WalletEntitlements.Premiums[0].ExpiresAtUnixSeconds);
            Assert.Equal(586, result.WalletEntitlements.Premiums[1].PremiumType);
            Assert.Equal(4_294_967_297L, result.Progression.HonorExperience);
            Assert.Equal(4_294_967_295L, result.Progression.GrowthCapsuleExperience);
            Assert.Equal([3, 7], result.Progression.LotteryProgress
                .Where(value => value.ItemTemplateId == 1001).Select(value => value.RewardIndex));
            Assert.Equal(7002, Assert.Single(result.Relations.Relations).StrikerSkillId);
            Assert.Equal(2_147_483_647, result.Relations.Unknown730Entries[3].SentinelOrValue);
            Assert.Equal([7, 7], result.Relations.PcRoomPlayTimeStates);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AccountEntryState_ApplicableCollectionsLoadAsEmpty()
    {
        var path = CreateDatabase();
        try
        {
            var repository = new SqliteAccountEntryStateRepository(path);
            await ExecuteAsync(path, """
                INSERT INTO accounts(account_id,m_id,password_hash) VALUES(52,'account-52','');
                INSERT INTO characters(character_id,account_id,name,level,slot_index,delete_flag)
                    VALUES(521,52,'one',1,0,0);
                INSERT INTO character_init_flags(character_id,pc_room_state) VALUES(521,0);
                """);

            var result = await repository.LoadAsync(new AccountId(52), CancellationToken.None);

            Assert.Empty(result.WalletEntitlements.Premiums);
            Assert.Null(result.Settings.MainGameOptions);
            Assert.Empty(result.Progression.LotteryProgress);
            Assert.Empty(result.Relations.Relations);
            Assert.Empty(result.Relations.Unknown730Entries);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("missing-account")]
    [InlineData("malformed-settings")]
    [InlineData("malformed-hotkey")]
    [InlineData("invalid-wallet")]
    [InlineData("wrong-owner-relation")]
    [InlineData("missing-relation-target")]
    [InlineData("invalid-lottery")]
    [InlineData("inconsistent-pc-room")]
    public async Task AccountEntryState_RejectsIncompleteMalformedOrCrossOwnerState(string invalidCase)
    {
        var path = CreateDatabase();
        try
        {
            var repository = new SqliteAccountEntryStateRepository(path);
            await ExecuteAsync(path, """
                INSERT INTO accounts(account_id,m_id,password_hash) VALUES(53,'account-53',''),(54,'other','');
                INSERT INTO account_settings(account_id,main_game_option) VALUES(53,X'0100');
                INSERT INTO characters(character_id,account_id,name,level,slot_index,delete_flag)
                    VALUES(531,53,'one',1,0,0),(532,53,'two',1,1,0),(541,54,'other',1,0,0);
                INSERT INTO character_init_flags(character_id,pc_room_state) VALUES(531,1),(532,1),(541,1);
                """);
            var sql = invalidCase switch
            {
                "missing-account" => "DELETE FROM accounts WHERE account_id=53;",
                "malformed-settings" => "UPDATE account_settings SET main_game_option=X'01' WHERE account_id=53;",
                "malformed-hotkey" => "UPDATE account_settings SET hotkey_slots=X'01' WHERE account_id=53;",
                "invalid-wallet" => "UPDATE accounts SET lucky_star=65536 WHERE account_id=53;",
                "wrong-owner-relation" => "INSERT INTO character_mercenary_support(owner_character_id,slot,support_character_id,skill_id,striker_skill_id) VALUES(531,0,541,1,2);",
                "missing-relation-target" => "INSERT INTO character_mercenary_support(owner_character_id,slot,support_character_id,skill_id,striker_skill_id) VALUES(531,0,999,1,2);",
                "invalid-lottery" => "INSERT INTO account_increase_chance_lottery_progress VALUES(53,10,20);",
                _ => "UPDATE character_init_flags SET pc_room_state=2 WHERE character_id=532;",
            };
            await ExecuteAsync(path, sql);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.LoadAsync(new AccountId(53), CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateDatabase() =>
        Path.Combine(Path.GetTempPath(), $"dfo-account-entry-{Guid.NewGuid():N}.db");
}
