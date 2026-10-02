using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Inventory;

internal sealed class SqliteInventoryExpansionStateTable(SqliteDatabase database) : IInventoryExpansionStateTable
{
    public async ValueTask<bool> ChangeCharacterAsync(CharacterId character, InventoryContainerType type,
        int expected, int value, CancellationToken ct)
    {
        await database.ExecuteAsync("""
            INSERT INTO character_container_state(character_id,list_type,list_param16)
            VALUES(@owner,@type,@initial) ON CONFLICT(character_id,list_type) DO NOTHING;
            """, p => { p.AddWithValue("@owner", character.Value); p.AddWithValue("@type", (int)type);
                p.AddWithValue("@initial", type == InventoryContainerType.PersonalCargo ? 8 : 0); }, ct).ConfigureAwait(false);
        return await database.ExecuteAsync("""
            UPDATE character_container_state SET list_param16=@value
            WHERE character_id=@owner AND list_type=@type AND list_param16=@expected;
            """, p => { p.AddWithValue("@owner", character.Value); p.AddWithValue("@type", (int)type);
                p.AddWithValue("@expected", expected); p.AddWithValue("@value", value); }, ct).ConfigureAwait(false) == 1;
    }
    public async ValueTask<bool> ChangeAccountAsync(AccountId account, int expected, int value, CancellationToken ct)
    {
        await database.ExecuteAsync("""
            INSERT INTO account_cargo_state(account_id,selection_key) VALUES(@owner,0)
            ON CONFLICT(account_id) DO NOTHING;
            """, p => p.AddWithValue("@owner", account.Value), ct).ConfigureAwait(false);
        return await database.ExecuteAsync("""
            UPDATE account_cargo_state SET selection_key=@value,updated_at=CURRENT_TIMESTAMP
            WHERE account_id=@owner AND selection_key=@expected;
            """, p => { p.AddWithValue("@owner", account.Value); p.AddWithValue("@expected", expected);
                p.AddWithValue("@value", value); }, ct).ConfigureAwait(false) == 1;
    }
    public async ValueTask<bool> ChangeAccountGoldAsync(AccountId account, int capacity, int expected, int value, CancellationToken ct)
    {
        if (value < 0) return false;
        return await database.ExecuteAsync("""
            UPDATE account_cargo_state SET value32=@value,updated_at=CURRENT_TIMESTAMP
            WHERE account_id=@owner AND selection_key=@capacity AND value32=@expected;
            """, p => { p.AddWithValue("@owner", account.Value); p.AddWithValue("@capacity", capacity);
                p.AddWithValue("@expected", expected); p.AddWithValue("@value", value); }, ct).ConfigureAwait(false) == 1;
    }
}
