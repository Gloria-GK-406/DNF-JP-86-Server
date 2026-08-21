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

/// <summary>The <c>character_gold_limits</c> table.</summary>
internal sealed class SqliteCharacterGoldLimitTable(SqliteDatabase database)
    : ICharacterGoldLimitTable
{
    public ValueTask<GoldLimitRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT gold_carry_limit, auction_gold_limit
            FROM character_gold_limits
            WHERE character_id = @characterId
            LIMIT 1;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            reader => new GoldLimitRow(reader.GetInt32(0), reader.GetInt32(1)),
            cancellationToken);
}

/// <summary>The <c>character_item_values</c> table.</summary>
/// <remarks>
/// The order is the one the client expects to receive these in -- cooldowns, then effects, then
/// by position within each. It is expressed in the statement because it is a property of the
/// list, and sorting it afterwards would read the rows twice to no purpose.
/// </remarks>
internal sealed class SqliteCharacterItemValueTable(SqliteDatabase database)
    : ICharacterItemValueTable
{
    public ValueTask<IReadOnlyList<ItemValueRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT character_id, list_kind, sort_order, item_id, value
            FROM character_item_values
            WHERE character_id = @characterId
            ORDER BY CASE list_kind WHEN 'cooltime' THEN 0 WHEN 'effect' THEN 1 ELSE 2 END,
                     sort_order;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            reader => new ItemValueRow(
                reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetInt32(4)),
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

/// <summary>The <c>character_rental_items</c> table.</summary>
internal sealed class SqliteCharacterRentalTable(SqliteDatabase database) : ICharacterRentalTable
{
    public ValueTask<IReadOnlyList<RentalRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT character_id, shop_entry_id, inventory_template_id, expire_time
            FROM character_rental_items
            WHERE character_id = @characterId;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            Rental,
            cancellationToken);

    internal static RentalRow Rental(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
}

/// <summary>The <c>rental_items</c> table.</summary>
internal sealed class SqliteLegacyRentalTable(SqliteDatabase database) : ILegacyRentalTable
{
    public ValueTask<IReadOnlyList<RentalRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT character_id, item_id, inventory_template_id, expire_time
            FROM rental_items
            WHERE character_id = @characterId;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            SqliteCharacterRentalTable.Rental,
            cancellationToken);
}

/// <summary>The <c>character_items</c> table, asked only whether it holds anything.</summary>
internal sealed class SqliteLegacyCharacterItemTable(SqliteDatabase database)
    : ILegacyCharacterItemTable
{
    public async ValueTask<bool> AnyAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        await database.ReadFirstAsync(
            """
            SELECT EXISTS(
                SELECT 1
                FROM character_items
                WHERE character_id = @characterId
                   OR (owner_scope = 'character' AND owner_id = @characterId));
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            reader => (object)(reader.GetInt32(0) != 0),
            cancellationToken).ConfigureAwait(false) is true;
}

/// <summary>The <c>account_cargo_items</c> table, asked only whether it holds anything.</summary>
internal sealed class SqliteLegacyAccountCargoTable(SqliteDatabase database)
    : ILegacyAccountCargoTable
{
    public async ValueTask<bool> AnyAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        await database.ReadFirstAsync(
            """
            SELECT EXISTS(
                SELECT 1
                FROM account_cargo_items
                WHERE account_id = @accountId);
            """,
            parameters => parameters.AddWithValue("@accountId", accountId.Value),
            reader => (object)(reader.GetInt32(0) != 0),
            cancellationToken).ConfigureAwait(false) is true;
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
