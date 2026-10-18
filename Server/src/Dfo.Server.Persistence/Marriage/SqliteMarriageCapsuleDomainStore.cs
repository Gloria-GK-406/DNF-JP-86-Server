using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Marriage;

internal sealed class SqliteMarriageCapsuleDomainStore(SqliteDatabase db) : IMarriageCapsuleDomainStore
{
    public async ValueTask<MarriageCapsuleState> ReadAsync(Guid relation, CharacterId character, CancellationToken ct) =>
        (await db.ReadAllAsync("SELECT energy,owned,pending FROM marriage_capsules WHERE relation_id=@r AND character_id=@c",
            p => { p.AddWithValue("@r", relation.ToString("N")); p.AddWithValue("@c", character.Value); },
            r => new MarriageCapsuleState(r.GetInt32(0), checked((byte)r.GetInt32(1)), checked((byte)r.GetInt32(2))), ct).ConfigureAwait(false)).SingleOrDefault() ?? new();
    public async ValueTask SaveAsync(Guid relation, CharacterId character, MarriageCapsuleState state, CancellationToken ct) =>
        _ = await db.ExecuteAsync("INSERT INTO marriage_capsules VALUES(@r,@c,@e,@o,@p) ON CONFLICT(relation_id,character_id) DO UPDATE SET energy=@e,owned=@o,pending=@p",
            p => { p.AddWithValue("@r", relation.ToString("N")); p.AddWithValue("@c", character.Value); p.AddWithValue("@e", state.Energy); p.AddWithValue("@o", state.Owned); p.AddWithValue("@p", state.Pending); }, ct).ConfigureAwait(false);
    public async ValueTask<bool> CreditAsync(Guid relation, CharacterId character, DungeonRunId run, int gain, int maximum, CancellationToken ct)
    {
        if (await db.ExecuteAsync("INSERT OR IGNORE INTO marriage_capsule_clears VALUES(@c,@run,@r)",
            p => { p.AddWithValue("@c", character.Value); p.AddWithValue("@run", run.Value.ToString("N")); p.AddWithValue("@r", relation.ToString("N")); }, ct).ConfigureAwait(false) != 1) return false;
        return await db.ExecuteAsync("INSERT INTO marriage_capsules VALUES(@r,@c,MIN(@gain,@max),0,0) ON CONFLICT(relation_id,character_id) DO UPDATE SET energy=MIN(energy+@gain,@max) WHERE energy<@max",
            p => { p.AddWithValue("@r", relation.ToString("N")); p.AddWithValue("@c", character.Value); p.AddWithValue("@gain", gain); p.AddWithValue("@max", maximum); }, ct).ConfigureAwait(false) == 1;
    }
}
