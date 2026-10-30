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
            SELECT account_id, selection_key, value32
            FROM account_cargo_state
            WHERE account_id = @accountId
            LIMIT 2;
            """,
            parameters => parameters.AddWithValue("@accountId", accountId.Value),
            reader => new AccountCargoStateRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
            cancellationToken);
}

/// <summary>The <c>character_stats.carried_gold</c> column.</summary>
/// <remarks>
/// Both directions are one statement: two changes answered at once must not each read the same
/// figure and each write their own sum. Nothing serialises them
/// -- the per-character execution gate this server declares has no callers -- and the item store,
/// being a unit of work, offers only read-modify-write. So the guard lives in the statement.
/// <para>
/// Every character has a row from creation, so neither direction opens one. A statement that
/// matched nothing means the character does not exist, which is not this table's to invent.
/// </para>
/// </remarks>
internal sealed class SqliteCharacterCarriedGoldTable(SqliteDatabase database)
    : ICharacterCarriedGoldTable
{
    /// <remarks>
    /// One statement over both tables, because the cap is worked out from the level and the level
    /// lives on the character while the figure and the stored upgrade live on its stats. Read
    /// apart, the level could move between the two reads and the cap would be priced at the wrong
    /// one.
    /// </remarks>
    public ValueTask<CarriedGoldRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT s.carried_gold, c.level, s.gold_carry_limit
            FROM character_stats s
            JOIN characters c ON c.character_id = s.character_id
            WHERE s.character_id = @characterId
            LIMIT 1;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            reader => new CarriedGoldRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
            cancellationToken);

    /// <remarks>
    /// The ceiling is a parameter of the statement rather than a comparison made afterwards, so a
    /// second addition answered at the same moment cannot carry the total past it.
    /// </remarks>
    public async ValueTask<int> AddAsync(
        CharacterId characterId, int amount, int ceiling, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ceiling);

        var rows = await database.ReadAllAsync(
            """
            UPDATE character_stats SET
                carried_gold = MAX(0, MIN(@ceiling, carried_gold + @amount))
            WHERE character_id = @characterId
            RETURNING carried_gold;
            """,
            parameters =>
            {
                parameters.AddWithValue("@characterId", characterId.Value);
                parameters.AddWithValue("@amount", amount);
                parameters.AddWithValue("@ceiling", ceiling);
            },
            reader => reader.GetInt32(0),
            cancellationToken).ConfigureAwait(false);

        return rows.Count > 0 ? rows[0] : 0;
    }

    /// <remarks>
    /// The condition that says the character can pay and the subtraction that makes it pay are one
    /// statement, so two charges answered at once cannot both pass a test only one of them could
    /// afford. A refusal writes nothing at all.
    /// <para>
    /// A take of zero is a charge of nothing and is always allowed. A negative amount is not: a
    /// negative take is an addition wearing the other name, and handing gold out is the one thing
    /// a charging seam must not be able to do by accident.
    /// </para>
    /// </remarks>
    public async ValueTask<int?> TryTakeAsync(
        CharacterId characterId, int amount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        var rows = await database.ReadAllAsync(
            """
            UPDATE character_stats SET
                carried_gold = carried_gold - @amount
            WHERE character_id = @characterId AND carried_gold >= @amount
            RETURNING carried_gold;
            """,
            parameters =>
            {
                parameters.AddWithValue("@characterId", characterId.Value);
                parameters.AddWithValue("@amount", amount);
            },
            reader => reader.GetInt32(0),
            cancellationToken).ConfigureAwait(false);

        return rows.Count > 0 ? rows[0] : null;
    }
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
internal sealed class SqliteCharacterRentalTable(SqliteDatabase database, TimeProvider clock) : ICharacterRentalTable
{
    public async ValueTask<IReadOnlyList<RentalRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var previous = await database.ReadAllAsync(
            """
            SELECT character_id, item_id, inventory_template_id, expire_time
            FROM character_rentals
            WHERE character_id = @characterId
            ORDER BY item_id;
            """,
            parameters => parameters.AddWithValue("@characterId", characterId.Value),
            reader => new RentalRow(
                reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)),
            cancellationToken).ConfigureAwait(false);
        var current = await database.ReadAllAsync(
            """
            SELECT r.character_id,r.item_uid,r.template_id,i.item_core
            FROM equipment_rental_instances r JOIN items i ON i.item_uid=r.item_uid
            JOIN item_slots s ON s.core_uid=i.item_uid AND s.owner_scope='character' AND s.owner_id=r.character_id
            WHERE r.character_id=@characterId ORDER BY r.item_uid
            """,
            p=>p.AddWithValue("@characterId",characterId.Value),
            r=>new RentalRow(r.GetInt32(0),r.GetInt64(1),r.GetInt64(2),InventoryItemCore.Decode((byte[])r.GetValue(3)).ExpireTime),
            cancellationToken).ConfigureAwait(false);
        var active=current.Where(x=>x.ExpireTime>(clock??TimeProvider.System).GetUtcNow().ToUnixTimeSeconds()).ToArray();
        return previous.Where(x=>!active.Any(y=>y.ItemId==x.ItemId || y.InventoryTemplateId==x.InventoryTemplateId))
            .Concat(active).OrderBy(x=>x.ItemId).ToArray();
    }
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
