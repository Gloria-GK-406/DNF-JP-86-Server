using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Inventory;

internal sealed class SqliteLuckyStarDomainStore(SqliteDatabase database) : ILuckyStarDomainStore
{
    public async ValueTask AcquireWriteAsync(CancellationToken ct) =>
        _ = await database.ExecuteAsync("UPDATE accounts SET lucky_star=lucky_star WHERE 0", _=>{},ct).ConfigureAwait(false);
    public async ValueTask<int?> ReadAsync(AccountId account,CancellationToken ct) =>
        await database.ReadFirstAsync("SELECT lucky_star FROM accounts WHERE account_id=@a",p=>p.AddWithValue("@a",account.Value),r=>(object)r.GetInt32(0),ct).ConfigureAwait(false) is int value ? value : null;
    public async ValueTask<bool> CompareExchangeAsync(AccountId account,int expected,int next,CancellationToken ct) =>
        await database.ExecuteAsync("UPDATE accounts SET lucky_star=@n WHERE account_id=@a AND lucky_star=@e AND @n>=0",
            p=>{p.AddWithValue("@a",account.Value);p.AddWithValue("@e",expected);p.AddWithValue("@n",next);},ct).ConfigureAwait(false)==1;
    public async ValueTask<bool> RecordClearAsync(DungeonRunId run,CharacterId character,CancellationToken ct) =>
        await database.ExecuteAsync("INSERT OR IGNORE INTO lucky_star_clear_rewards(run_id,character_id) VALUES(@r,@c)",
            p=>{p.AddWithValue("@r",run.Value.ToString("N"));p.AddWithValue("@c",character.Value);},ct).ConfigureAwait(false)==1;
}
internal sealed class SqliteEquipmentRentalDomainStore(SqliteDatabase database) : IEquipmentRentalDomainStore
{
    public ValueTask<IReadOnlyList<EquipmentRentalLink>> ReadAsync(CharacterId character,CancellationToken ct) =>
        database.ReadAllAsync("SELECT item_uid,template_id FROM equipment_rental_instances WHERE character_id=@c ORDER BY item_uid",
            p=>p.AddWithValue("@c",character.Value),r=>new EquipmentRentalLink(new(r.GetInt32(0)),r.GetInt32(1)),ct);
    public async ValueTask AddAsync(CharacterId character,ItemIdentity uid,int itemId,CancellationToken ct) =>
        _ = await database.ExecuteAsync("INSERT INTO equipment_rental_instances(character_id,item_uid,template_id) VALUES(@c,@u,@t)",
            p=>{p.AddWithValue("@c",character.Value);p.AddWithValue("@u",uid.Value);p.AddWithValue("@t",itemId);},ct).ConfigureAwait(false);
}
