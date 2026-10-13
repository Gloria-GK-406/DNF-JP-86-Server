using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

internal sealed class SqliteDespairTowerDomainStore(SqliteDatabase database) : IDespairTowerDomainStore
{
    public async ValueTask<DespairTowerProgress> ReadAsync(CharacterId character, CancellationToken ct) =>
        (await database.ReadAllAsync("SELECT highest_cleared_floor FROM character_despair_tower WHERE character_id=@id",
            p => p.AddWithValue("@id", character.Value), r => new DespairTowerProgress(r.GetInt32(0)), ct).ConfigureAwait(false)).SingleOrDefault() ?? new();
    public async ValueTask<bool> TryAdvanceAsync(CharacterId character, int expectedFloor, Guid runId, CancellationToken ct)
    {
        if (expectedFloor is < 0 or >= 100 || runId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(expectedFloor));
        return await database.ExecuteAsync("""
            INSERT INTO character_despair_tower(character_id,highest_cleared_floor,last_clear_run)
            SELECT @id,@next,@run WHERE @old=0
            ON CONFLICT(character_id) DO UPDATE SET highest_cleared_floor=@next,last_clear_run=@run
            WHERE highest_cleared_floor=@old AND last_clear_run<>@run
            """, p => { p.AddWithValue("@id", character.Value); p.AddWithValue("@old", expectedFloor);
                p.AddWithValue("@next", expectedFloor + 1); p.AddWithValue("@run", runId.ToString("N")); }, ct).ConfigureAwait(false) == 1
            || expectedFloor > 0 && await database.ExecuteAsync("""
                UPDATE character_despair_tower SET highest_cleared_floor=@next,last_clear_run=@run
                WHERE character_id=@id AND highest_cleared_floor=@old AND last_clear_run<>@run
                """, p => { p.AddWithValue("@id", character.Value); p.AddWithValue("@old", expectedFloor);
                    p.AddWithValue("@next", expectedFloor + 1); p.AddWithValue("@run", runId.ToString("N")); }, ct).ConfigureAwait(false) == 1;
    }
}
