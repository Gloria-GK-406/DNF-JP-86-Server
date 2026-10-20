using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

internal sealed class SqliteSouthernDaleDomainStore(SqliteDatabase database) : ISouthernDaleDomainStore
{
    public async ValueTask<bool> HasCompletedDailyQuestAsync(CharacterId character, DateOnly period, CancellationToken ct) =>
        (await database.ReadAllAsync("SELECT 1 FROM southern_dale_daily_quest WHERE character_id=@character AND period=@period",
            p => { p.AddWithValue("@character", character.Value); p.AddWithValue("@period", period.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)); },
            row => row.GetInt32(0), ct).ConfigureAwait(false)).Count != 0;

    public async ValueTask<bool> TryCompleteDailyQuestAsync(CharacterId character, DateOnly period, CancellationToken ct) =>
        await database.ExecuteAsync("INSERT OR IGNORE INTO southern_dale_daily_quest(character_id,period) VALUES(@character,@period)",
            p => { p.AddWithValue("@character", character.Value); p.AddWithValue("@period", period.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)); }, ct).ConfigureAwait(false) == 1;

    public async ValueTask<bool> TryClaimClearAsync(Guid runId, CharacterId character, CancellationToken ct)
    {
        if (runId == Guid.Empty || character.Value <= 0) throw new ArgumentException("Invalid Southern Dale clear identity.");
        return await database.ExecuteAsync("INSERT OR IGNORE INTO southern_dale_clear_rewards(run_id,character_id) VALUES(@run,@character)",
            p => { p.AddWithValue("@run", runId.ToString("N")); p.AddWithValue("@character", character.Value); }, ct)
            .ConfigureAwait(false) == 1;
    }
}
