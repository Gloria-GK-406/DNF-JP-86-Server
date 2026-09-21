using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Accounts;

/// <summary>
/// Everything an account is loaded with, written down before the load is taken apart.
/// </summary>
/// <remarks>
/// Eight tables become one record here, and three of the reads are joins whose ordering and
/// cross-table checks are part of the answer. Splitting those joins into per-table reads means
/// re-deriving both in the domain, and the way that goes wrong is quiet: a list in a different
/// order, or a check that no longer refuses what it used to.
///
/// So the record is written out whole and compared as text, and every seeded value is distinct so
/// that two columns swapping cannot hide.
/// </remarks>
public sealed class AccountEntryLoadCharacterizationTests
{
    [Fact]
    public async Task AnAccountIsLoadedWithExactlyWhatItWasLoadedWithBefore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-account-pin-{Guid.NewGuid():N}.db");
        try
        {
            var repository = Assemble(path);
            await SeedAsync(path);

            var loaded = await repository.LoadAsync(new AccountId(12), CancellationToken.None);

            Assert.Equal(Expected.ReplaceLineEndings("\n") + "\n", StorageRecordDump.Of(loaded));
        }
        finally
        {
            Clean(path);
        }
    }

    /// <remarks>
    /// The settings-only path exists because logging in has to read them before any character is
    /// there to be loaded. It reads one of the eight tables and refuses none of the others.
    /// </remarks>
    [Fact]
    public async Task AccountSettingsAloneAreLoadedWithExactlyWhatTheyWereLoadedWithBefore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-account-settings-{Guid.NewGuid():N}.db");
        try
        {
            var repository = Assemble(path);
            await SeedAsync(path);

            var loaded = await repository.LoadSettingsAsync(
                new AccountId(12), CancellationToken.None);

            Assert.Equal(ExpectedSettings.ReplaceLineEndings("\n") + "\n", StorageRecordDump.Of(loaded));
        }
        finally
        {
            Clean(path);
        }
    }

    /// <summary>A refusal the golden cannot express, because a refusal produces no record.</summary>
    /// <remarks>
    /// The join used to establish this by reaching for the supported character's own row and
    /// checking whose it was. Reading the tables separately means the roster has to establish it
    /// instead -- and if that check is simply not written, every load of well-formed data still
    /// succeeds. Only ill-formed data shows the difference, so ill-formed data is what this uses.
    /// </remarks>
    [Fact]
    public async Task ARelationPointingOutsideTheAccountIsRefused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-account-foreign-{Guid.NewGuid():N}.db");
        try
        {
            var repository = Assemble(path);
            await SeedAsync(path);
            await ExecuteAsync(
                path,
                "INSERT INTO characters(character_id,account_id,name,job,level,slot_index,"
                + "delete_flag) VALUES(40,13,'stranger',1,60,0,0);"
                + "UPDATE character_mercenary_support SET support_character_id=40 "
                + "WHERE owner_character_id=30;");

            var refusal = await Assert.ThrowsAsync<InvalidDataException>(
                async () => await repository.LoadAsync(new AccountId(12), CancellationToken.None));

            Assert.Contains("missing, deleted or foreign", refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            Clean(path);
        }
    }

    /// <summary>Another refusal the golden cannot hold, for the same reason.</summary>
    /// <remarks>
    /// These columns are the client's own bytes, kept and handed back. A row that has grown past
    /// anything reasonable is refused rather than read, and a golden made of well-formed data
    /// cannot say whether that refusal is still there.
    /// </remarks>
    [Fact]
    public async Task SettingsTooLargeToBeReasonableAreRefused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-account-huge-{Guid.NewGuid():N}.db");
        try
        {
            var repository = Assemble(path);
            await SeedAsync(path);
            await ExecuteBlobAsync(
                path,
                "UPDATE account_settings SET quickchat_bank0=@blob WHERE account_id=12;",
                new byte[(64 * 1024) + 1]);

            var refusal = await Assert.ThrowsAsync<InvalidDataException>(
                async () => await repository.LoadSettingsAsync(
                    new AccountId(12), CancellationToken.None));

            Assert.Contains("exceeds the supported size", refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            Clean(path);
        }
    }

    private static async Task ExecuteBlobAsync(string path, string sql, byte[] blob)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@blob", blob);
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

    /// <summary>The account load as it is put together today: one repository per table.</summary>
    private static AccountEntryAssembly Assemble(string path)
    {
        var database = new SqliteDatabase(path, new DomainTransactionScope());
        return new AccountEntryAssembly(
            new SqliteAccountTable(database),
            new SqliteAccountSettingsTable(database),
            new PremiumServiceDomainService(new SqliteAccountPremiumTable(database), new DomainTransactionScope(), TimeProvider.System),
            new SqliteAccountLotteryProgressTable(database),
            new SqliteCharacterTable(database),
            new SqliteCharacterConditionTable(database),
            new SqliteCharacterBuyRestrictTable(database),
            new SqliteCharacterMercenarySupportTable(database));
    }

    /// <summary>
    /// Two characters, so that anything ordered by roster position has something to order.
    /// </summary>
    /// <remarks>
    /// The higher character id sits in the earlier slot, so ordering by id and ordering by slot
    /// give different answers. Without that, a read that lost the slot ordering would look
    /// identical to one that kept it.
    /// </remarks>
    private static async Task SeedAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts(account_id,m_id,password_hash,cera,token_cera,happy_token_cera,
                lucky_star,honor_exp,growth_capsule_exp,created_at)
                VALUES(12,'pinned','hash',1001,1002,1003,1004,1005,1006,'2020-01-02 03:04:05');
            INSERT INTO account_settings(account_id,main_game_option,quickchat_bank0,
                quickchat_bank1,hotkey_key_type,hotkey_slots)
                VALUES(12,X'A0A1',X'A2',X'A3',101,X'A4A5A6');
            INSERT INTO account_premiums(account_id,premium_type,end_time)
                VALUES(12,1009,1010),(12,1007,1008);
            INSERT INTO account_increase_chance_lottery_progress(account_id,item_template_id,reward_index)
                VALUES(12,1012,4),(12,1011,3);
            INSERT INTO characters(character_id,account_id,name,job,level,slot_index,delete_flag)
                VALUES(31,12,'earlier',1,102,5,0);
            INSERT INTO characters(character_id,account_id,name,job,level,slot_index,delete_flag)
                VALUES(30,12,'later',1,103,7,0);
            INSERT INTO character_condition(character_id,pc_room_state) VALUES(30,104),(31,104);
            INSERT INTO character_buy_restrict_items(character_id,sort_order,entry_id,
                sentinel_or_value,flag)
                VALUES(31,1013,1014,1015,1016),(30,1017,1018,1019,1020);
            INSERT INTO character_mercenary_support(owner_character_id,slot,support_character_id,
                skill_id,striker_skill_id)
                VALUES(31,105,30,1021,1022),(30,106,31,1023,1024);
            """;
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

    /// <summary>What the load produced before any of it moved.</summary>
    private const string Expected =
        """
        AccountId.Value = 12
        Settings.MainGameOptions = 0xA0A1
        Settings.QuickChatBank0 = 0xA2
        Settings.QuickChatBank1 = 0xA3
        Settings.HotkeyKeyType = 101
        Settings.AccountHotkeySlotPrefix = 0xA4A5
        WalletEntitlements.Cera = 1001
        WalletEntitlements.TokenCera = 1002
        WalletEntitlements.HappyTokenCera = 1003
        WalletEntitlements.LuckyStar = 1004
        WalletEntitlements.Premiums[0].Item1 = 1007
        WalletEntitlements.Premiums[0].Item2 = 1008
        WalletEntitlements.Premiums[1].Item1 = 1009
        WalletEntitlements.Premiums[1].Item2 = 1010
        Progression.HonorExperience = 1005
        Progression.GrowthCapsuleExperience = 1006
        Progression.Roster[0].Item1 = 31
        Progression.Roster[0].Item2 = 5
        Progression.Roster[0].Item3 = 102
        Progression.Roster[1].Item1 = 30
        Progression.Roster[1].Item2 = 7
        Progression.Roster[1].Item3 = 103
        Progression.LotteryProgress[0].Item1 = 1011
        Progression.LotteryProgress[0].Item2 = 3
        Progression.LotteryProgress[1].Item1 = 1012
        Progression.LotteryProgress[1].Item2 = 4
        Relations.Relations[0].Item1 = 31
        Relations.Relations[0].Item2 = 105
        Relations.Relations[0].Item3 = 30
        Relations.Relations[0].Item4 = 1021
        Relations.Relations[0].Item5 = 1022
        Relations.Relations[1].Item1 = 30
        Relations.Relations[1].Item2 = 106
        Relations.Relations[1].Item3 = 31
        Relations.Relations[1].Item4 = 1023
        Relations.Relations[1].Item5 = 1024
        Relations.Unknown730Entries[0].Item1 = 5
        Relations.Unknown730Entries[0].Item2 = 1013
        Relations.Unknown730Entries[0].Item3 = 1014
        Relations.Unknown730Entries[0].Item4 = 1015
        Relations.Unknown730Entries[0].Item5 = 1016
        Relations.Unknown730Entries[1].Item1 = 7
        Relations.Unknown730Entries[1].Item2 = 1017
        Relations.Unknown730Entries[1].Item3 = 1018
        Relations.Unknown730Entries[1].Item4 = 1019
        Relations.Unknown730Entries[1].Item5 = 1020
        Relations.PcRoomPlayTimeStates[0] = 104
        Relations.PcRoomPlayTimeStates[1] = 104
        """;

    private const string ExpectedSettings =
        """
        MainGameOptions = 0xA0A1
        QuickChatBank0 = 0xA2
        QuickChatBank1 = 0xA3
        HotkeyKeyType = 101
        AccountHotkeySlotPrefix = 0xA4A5
        """;
}
