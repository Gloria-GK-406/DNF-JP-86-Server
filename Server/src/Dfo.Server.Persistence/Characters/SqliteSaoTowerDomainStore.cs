using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

internal sealed class SqliteSaoTowerDomainStore(SqliteDatabase database) : ISaoTowerDomainStore
{
    public async ValueTask<SaoTowerProgress> ReadAsync(string season, CancellationToken ct) =>
        (await database.ReadAllAsync("SELECT round,next_floor,revision FROM sao_tower_progress WHERE season=@season",
            p => p.AddWithValue("@season", season), r => new SaoTowerProgress(season, r.GetInt32(0), r.GetInt32(1), r.GetInt64(2)), ct)
            .ConfigureAwait(false)).SingleOrDefault() ?? new(season);

    public async ValueTask<bool> TryClearAsync(SaoTowerProgress expected, Guid runId, int maximumFloor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expected.Season) || expected.Round < 1 || expected.Revision < 0
            || maximumFloor is < 1 or > 100 || expected.NextFloor < 1 || expected.NextFloor > maximumFloor
            || runId == Guid.Empty) throw new ArgumentException("Invalid SAO clear identity.");
        // Seed the shared row without overwriting a concurrent winner or a later season revision.
        await database.ExecuteAsync("INSERT OR IGNORE INTO sao_tower_progress(season,round,next_floor,revision) VALUES(@season,1,1,0)",
            p => p.AddWithValue("@season", expected.Season), ct).ConfigureAwait(false);
        var wrapped = expected.NextFloor == maximumFloor;
        var changed = await database.ExecuteAsync("""
            UPDATE sao_tower_progress SET round=@nextRound,next_floor=@nextFloor,revision=revision+1
            WHERE season=@season AND round=@round AND next_floor=@floor AND revision=@revision
            AND NOT EXISTS(SELECT 1 FROM sao_first_clears WHERE run_id=@run)
            """, p =>
        {
            p.AddWithValue("@season", expected.Season); p.AddWithValue("@round", expected.Round);
            p.AddWithValue("@floor", expected.NextFloor); p.AddWithValue("@revision", expected.Revision);
            p.AddWithValue("@nextRound", wrapped ? checked(expected.Round + 1) : expected.Round);
            p.AddWithValue("@nextFloor", wrapped ? 1 : expected.NextFloor + 1);
            p.AddWithValue("@run", runId.ToString("N"));
        }, ct).ConfigureAwait(false);
        if (changed != 1) return false;
        await database.ExecuteAsync("INSERT INTO sao_first_clears(season,round,floor,run_id) VALUES(@season,@round,@floor,@run)",
            p => { p.AddWithValue("@season", expected.Season); p.AddWithValue("@round", expected.Round);
                p.AddWithValue("@floor", expected.NextFloor); p.AddWithValue("@run", runId.ToString("N")); }, ct).ConfigureAwait(false);
        return true;
    }

    public async ValueTask RecordRewardAsync(Guid runId, CharacterId character, int itemId, CancellationToken ct)
    {
        if (await database.ExecuteAsync("INSERT INTO sao_first_clear_rewards(run_id,character_id,item_id) VALUES(@run,@char,@item)",
            p => { p.AddWithValue("@run", runId.ToString("N")); p.AddWithValue("@char", character.Value); p.AddWithValue("@item", itemId); }, ct)
            .ConfigureAwait(false) != 1) throw new InvalidOperationException("SAO reward could not be recorded.");
    }
}
