using System.Globalization;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Accounts;

/// <summary>The <c>accounts</c> table.</summary>
/// <remarks>
/// The checks here are about whether a row can be read as what it claims to be -- a balance that
/// does not fit where it is going, a timestamp that is not one. They belong beside the read
/// because they are about this table's own rows; what an account may then do with those values is
/// not decided here.
/// </remarks>
internal sealed class SqliteAccountTable(SqliteDatabase database) : IAccountTable
{
    public ValueTask InsertIfAbsentAsync(
        string loginId, string passwordHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loginId);
        ArgumentNullException.ThrowIfNull(passwordHash);
        return Ignore(database.ExecuteAsync(
            """
            INSERT INTO accounts (m_id, password_hash)
            VALUES (@loginId, @passwordHash)
            ON CONFLICT(m_id) DO NOTHING;
            """,
            parameters =>
            {
                parameters.AddWithValue("@loginId", loginId);
                parameters.AddWithValue("@passwordHash", passwordHash);
            },
            cancellationToken));

        static async ValueTask Ignore(ValueTask<int> touched) => _ = await touched;
    }

    public ValueTask<AccountRecord?> FindByLoginIdAsync(
        string loginId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loginId);
        return database.ReadFirstAsync(
            """
            SELECT account_id, m_id, password_hash, created_at
            FROM accounts
            WHERE m_id = @loginId;
            """,
            parameters => parameters.AddWithValue("@loginId", loginId),
            reader => new AccountRecord(
                new AccountId(reader.GetInt32(0)),
                reader.GetString(1),
                reader.GetString(2),
                CreatedAt(reader.GetString(3))),
            cancellationToken);
    }

    public ValueTask<AccountWalletRow?> FindWalletAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT cera,token_cera,happy_token_cera,lucky_star,honor_exp,growth_capsule_exp
            FROM accounts WHERE account_id=@aid;
            """,
            parameters => parameters.AddWithValue("@aid", accountId.Value),
            Wallet,
            cancellationToken);

    public async ValueTask<int?> FindLuckyStarAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        await database.ReadFirstAsync(
            "SELECT lucky_star FROM accounts WHERE account_id=@aid",
            parameters => parameters.AddWithValue("@aid", accountId.Value),
            reader => (object)reader.GetInt32(0),
            cancellationToken).ConfigureAwait(false) is int luckyStar ? luckyStar : null;

    public ValueTask<CubeFragmentRow?> FindCubeFragmentsAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT cube_black, cube_white, cube_red, cube_blue, cube_clear, cube_gold
            FROM accounts
            WHERE account_id = @accountId
            LIMIT 1;
            """,
            parameters => parameters.AddWithValue("@accountId", accountId.Value),
            reader => new CubeFragmentRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)),
            cancellationToken);

    public ValueTask<int> SetLuckyStarAsync(
        AccountId accountId, int luckyStar, CancellationToken cancellationToken) =>
        database.ExecuteAsync(
            "UPDATE accounts SET lucky_star=@total WHERE account_id=@aid",
            parameters =>
            {
                parameters.AddWithValue("@total", luckyStar);
                parameters.AddWithValue("@aid", accountId.Value);
            },
            cancellationToken);

    public ValueTask<AccountExperienceRow?> FindExperienceAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            "SELECT honor_exp,growth_capsule_exp FROM accounts WHERE account_id=@aid",
            parameters => parameters.AddWithValue("@aid", accountId.Value),
            reader => new AccountExperienceRow(reader.GetInt64(0), reader.GetInt64(1)),
            cancellationToken);

    public ValueTask<int> SetExperienceAsync(
        AccountId accountId,
        long honourExperience,
        long growthCapsuleExperience,
        CancellationToken cancellationToken) =>
        database.ExecuteAsync(
            "UPDATE accounts SET honor_exp=@honor,growth_capsule_exp=@capsule WHERE account_id=@aid",
            parameters =>
            {
                parameters.AddWithValue("@honor", honourExperience);
                parameters.AddWithValue("@capsule", growthCapsuleExperience);
                parameters.AddWithValue("@aid", accountId.Value);
            },
            cancellationToken);

    private static AccountWalletRow Wallet(SqliteDataReader reader)
    {
        var row = new AccountWalletRow(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
        if (row.Cera is < 0 or > int.MaxValue || row.TokenCera is < 0 or > int.MaxValue
            || row.HappyTokenCera is < 0 or > int.MaxValue
            || row.LuckyStar is < 0 or > ushort.MaxValue
            || row.HonorExperience < 0 || row.GrowthCapsuleExperience is < 0 or > uint.MaxValue)
        {
            throw new InvalidDataException("Account wallet or progression value is out of range.");
        }

        return row;
    }

    private static DateTimeOffset CreatedAt(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var createdAt)
            ? createdAt
            : throw new InvalidOperationException($"Account created_at value '{value}' is invalid.");
}

/// <summary>The <c>account_settings</c> table.</summary>
internal sealed class SqliteAccountSettingsTable(SqliteDatabase database) : IAccountSettingsTable
{
    public ValueTask<AccountSettingsStorageRecord?> FindAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT main_game_option,quickchat_bank0,quickchat_bank1,hotkey_key_type,hotkey_slots
            FROM account_settings WHERE account_id=@aid;
            """,
            parameters => parameters.AddWithValue("@aid", accountId.Value),
            Settings,
            cancellationToken);

    private static AccountSettingsStorageRecord Settings(SqliteDataReader reader)
    {
        var main = OptionalBlob(reader, 0, "main game options");
        var bank0 = Blob(reader, 1, "quick-chat bank 0");
        var bank1 = Blob(reader, 2, "quick-chat bank 1");
        var hotkeyType = reader.GetInt32(3);
        var hotkeys = Blob(reader, 4, "account hotkeys");
        if (main is not null && (main.Length == 0 || main.Length % 2 != 0)
            || hotkeys.Length == 1
            || hotkeyType is < 0 or > byte.MaxValue)
        {
            throw new InvalidDataException("Account settings are malformed.");
        }

        return new AccountSettingsStorageRecord(
            main, bank0, bank1, hotkeyType, hotkeys.Length >= 2 ? hotkeys[..2] : []);
    }

    /// <summary>How much of one of these a row is allowed to hold.</summary>
    /// <remarks>
    /// These columns come straight from the client and go straight back to it. A limit here is
    /// what stops a row that has grown past anything reasonable from being read at all, rather
    /// than being discovered further along where the size is no longer the obvious cause.
    /// </remarks>
    private const int MaximumBytes = 64 * 1024;

    private static byte[] Blob(SqliteDataReader reader, int ordinal, string name) =>
        OptionalBlob(reader, ordinal, name) ?? [];

    private static byte[]? OptionalBlob(SqliteDataReader reader, int ordinal, string name)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        // Cast rather than test: a column declared BLOB that holds something else is not a case
        // this decides what to do about, and the cast says so as clearly as anything written here
        // would. Kept as it was for the same reason the message below is.
        var value = (byte[])reader.GetValue(ordinal);
        return value.Length <= MaximumBytes
            ? value
            : throw new InvalidDataException($"{name} exceeds the supported size.");
    }
}

/// <summary>The <c>account_premiums</c> table.</summary>
internal sealed class SqliteAccountPremiumTable(SqliteDatabase database) : IAccountPremiumTable
{
    public ValueTask<IReadOnlyList<AccountPremiumRow>> ReadAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT premium_type,end_time FROM account_premiums WHERE account_id=@aid "
            + "ORDER BY premium_type",
            parameters => parameters.AddWithValue("@aid", accountId.Value),
            reader => reader.GetInt64(1) >= 0
                ? new AccountPremiumRow(reader.GetInt32(0), reader.GetInt64(1))
                : throw new InvalidDataException("Account premium state is out of range."),
            cancellationToken);
}

/// <summary>The <c>account_increase_chance_lottery_progress</c> table.</summary>
internal sealed class SqliteAccountLotteryProgressTable(SqliteDatabase database)
    : IAccountLotteryProgressTable
{
    public async ValueTask<IReadOnlyList<AccountLotteryRow>> ReadAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        var rows = await database.ReadAllAsync(
            """
            SELECT item_template_id,reward_index FROM account_increase_chance_lottery_progress
            WHERE account_id=@aid ORDER BY item_template_id,reward_index
            """,
            parameters => parameters.AddWithValue("@aid", accountId.Value),
            Row,
            cancellationToken).ConfigureAwait(false);
        // A fact about the whole table rather than about a row, so it waits until they are all here.
        return rows.Distinct().Count() == rows.Count
            ? rows
            : throw new InvalidDataException("Account lottery progress contains duplicates.");
    }

    private static AccountLotteryRow Row(SqliteDataReader reader)
    {
        var row = new AccountLotteryRow(reader.GetInt32(0), reader.GetInt32(1));
        return row.ItemTemplateId > 0 && row.RewardIndex is >= 0 and < 20
            ? row
            : throw new InvalidDataException("Account lottery progress is out of range.");
    }
}
