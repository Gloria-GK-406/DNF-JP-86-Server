using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Inventory;

/// <summary>The <c>character_container_state</c> table.</summary>
internal sealed class SqliteCharacterContainerStateTable(SqliteDatabase database)
    : ICharacterContainerStateTable
{
    public ValueTask<IReadOnlyList<ContainerStateRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT character_id, list_type, list_param16
            FROM character_container_state
            WHERE character_id = @characterId
            ORDER BY list_type;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            reader => new ContainerStateRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
            cancellationToken);

    public async ValueTask InsertIfAbsentAsync(
        CharacterId characterId,
        int listType,
        int parameter,
        CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            """
            INSERT INTO character_container_state (character_id, list_type, list_param16)
            VALUES (@characterId, @listType, @parameter)
            ON CONFLICT(character_id, list_type) DO NOTHING;
            """,
            parameters =>
            {
                parameters.AddWithValue("@characterId", characterId.Value);
                parameters.AddWithValue("@listType", listType);
                parameters.AddWithValue("@parameter", parameter);
            },
            cancellationToken).ConfigureAwait(false);
}

/// <summary>The <c>account_cargo_state</c> table.</summary>
/// <remarks>
/// Two rows are read where one is expected. There should never be a second -- but if there is,
/// the caller has to be able to say so, and a statement that stopped at one would make that
/// impossible to see.
/// </remarks>
internal sealed class SqliteAccountCargoStateTable(SqliteDatabase database)
    : IAccountCargoStateTable
{
    public ValueTask<IReadOnlyList<AccountCargoStateRow>> ReadByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT account_id, selection_key, value32, item_count
            FROM account_cargo_state
            WHERE account_id = @accountId
            LIMIT 2;
            """,
            parameters => parameters.AddWithValue("@accountId", accountId.Value),
            reader => new AccountCargoStateRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
            cancellationToken);
}

/// <summary>The gold caps, read from <c>character_stats</c> where they now live.</summary>
internal sealed class SqliteCharacterGoldLimitTable(SqliteDatabase database)
    : ICharacterGoldLimitTable
{
    public ValueTask<GoldLimitRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT gold_carry_limit, auction_gold_limit
            FROM character_stats
            WHERE character_id = @characterId
            LIMIT 1;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            reader => new GoldLimitRow(reader.GetInt32(0), reader.GetInt32(1)),
            cancellationToken);
}

/// <summary>The <c>character_item_cooldowns</c> table.</summary>
/// <remarks>
/// The client expects cooldowns before effects and each by position within its own list. Half of
/// that order used to be a CASE over a column whose value said which kind of thing a row was; the
/// half that is about position stays in the statement, and the half that was about kind is now the
/// order the two reads are made in.
/// </remarks>
internal sealed class SqliteCharacterItemCooldownTable(SqliteDatabase database)
    : ICharacterItemCooldownTable
{
    public ValueTask<IReadOnlyList<ItemValueRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT character_id, sort_order, item_id, value
            FROM character_item_cooldowns
            WHERE character_id = @characterId
            ORDER BY sort_order;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            ItemValue,
            cancellationToken);

    internal static ItemValueRow ItemValue(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
}

/// <summary>The <c>character_item_effects</c> table.</summary>
internal sealed class SqliteCharacterItemEffectTable(SqliteDatabase database)
    : ICharacterItemEffectTable
{
    public ValueTask<IReadOnlyList<ItemValueRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT character_id, sort_order, item_id, value
            FROM character_item_effects
            WHERE character_id = @characterId
            ORDER BY sort_order;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            SqliteCharacterItemCooldownTable.ItemValue,
            cancellationToken);
}

/// <summary>The <c>character_item_locks</c> table.</summary>
internal sealed class SqliteCharacterItemLockTable(SqliteDatabase database)
    : ICharacterItemLockTable
{
    public ValueTask<IReadOnlyList<ItemLockRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT character_id, equipment_lock_id, inventory_list_type,
                   slot, state, COALESCE(remaining_seconds, 0)
            FROM character_item_locks
            WHERE character_id = @characterId
            ORDER BY equipment_lock_id;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            reader => new ItemLockRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)),
            cancellationToken);
}

/// <summary>The <c>character_rentals</c> table.</summary>
/// <remarks>
/// One table where there were two, whose columns said the same things under different names and
/// which the load already read into one list. The order is stated because a single table can no
/// longer get it from which of the two a row came out of.
/// </remarks>
internal sealed class SqliteCharacterRentalTable(SqliteDatabase database) : ICharacterRentalTable
{
    public ValueTask<IReadOnlyList<RentalRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT character_id, item_id, inventory_template_id, expire_time
            FROM character_rentals
            WHERE character_id = @characterId
            ORDER BY item_id;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            reader => new RentalRow(
                reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)),
            cancellationToken);
}

/// <summary>The <c>character_daily_counters</c> table.</summary>
internal sealed class SqliteCharacterDailyCounterTable(SqliteDatabase database)
    : ICharacterDailyCounterTable
{
    /// <remarks>
    /// No row and a row whose day is empty answer the same way, as they always have: neither is
    /// a day that has been claimed.
    /// </remarks>
    public async ValueTask<string?> FindClaimedPeriodAsync(
        CharacterId characterId, string counterKey, CancellationToken cancellationToken) =>
        (await database.ReadAllAsync(
            "SELECT period FROM character_daily_counters "
            + "WHERE character_id=@cid AND counter_key=@key AND value>0",
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@key", counterKey);
            },
            reader => reader.IsDBNull(0) ? null : reader.GetString(0),
            cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    public ValueTask<int> ClaimAsync(
        CharacterId characterId,
        string counterKey,
        string period,
        CancellationToken cancellationToken) =>
        database.ExecuteAsync(
            """
            INSERT INTO character_daily_counters(character_id,counter_key,period,value) VALUES(@cid,@key,@period,1)
            ON CONFLICT(character_id,counter_key) DO UPDATE SET period=excluded.period,value=1
            """,
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@key", counterKey);
                parameters.AddWithValue("@period", period);
            },
            cancellationToken);
}
