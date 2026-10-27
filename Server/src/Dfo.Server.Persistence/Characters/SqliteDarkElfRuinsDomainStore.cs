using System.Text.Json;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

internal sealed class SqliteDarkElfRuinsDomainStore(SqliteDatabase database) : IDarkElfRuinsDomainStore
{
    public async ValueTask<DarkElfRuinsProgress> ReadAsync(CharacterId character, CancellationToken ct) =>
        (await database.ReadAllAsync("SELECT license,clears,day,entries,month,monthly_entries,guardians,revision FROM darkelf_ruins_progress WHERE character_id=@character",
            p => p.AddWithValue("@character", character.Value), r => new DarkElfRuinsProgress(r.GetInt32(0), r.GetInt32(1),
                DateOnly.ParseExact(r.GetString(2), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), r.GetInt32(3),
                DateOnly.ParseExact(r.GetString(4), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                r.GetInt32(5), r.GetInt32(6), r.GetInt64(7)), ct)
            .ConfigureAwait(false)).SingleOrDefault() ?? new();

    public async ValueTask<bool> CompareExchangeAsync(CharacterId character, DarkElfRuinsProgress expected,
        DarkElfRuinsProgress replacement, CancellationToken ct)
    {
        if (replacement.Revision != expected.Revision + 1) throw new ArgumentException("Ruins revision must advance exactly once.");
        if (expected.Revision == 0)
            await database.ExecuteAsync("INSERT OR IGNORE INTO darkelf_ruins_progress(character_id) VALUES(@character)",
                p => p.AddWithValue("@character", character.Value), ct).ConfigureAwait(false);
        return await database.ExecuteAsync("""
            UPDATE darkelf_ruins_progress SET revision=@next,license=@license,clears=@clears,day=@day,entries=@entries,
                month=@month,monthly_entries=@monthly,guardians=@guardians WHERE character_id=@character AND revision=@expected
            """,
            p => { p.AddWithValue("@character", character.Value); p.AddWithValue("@expected", expected.Revision);
                p.AddWithValue("@next", replacement.Revision); p.AddWithValue("@license", replacement.License);
                p.AddWithValue("@clears", replacement.Clears); p.AddWithValue("@day", Date(replacement.Day));
                p.AddWithValue("@entries", replacement.Entries); p.AddWithValue("@month", Date(replacement.Month));
                p.AddWithValue("@monthly", replacement.MonthlyEntries); p.AddWithValue("@guardians", replacement.Guardians); }, ct)
            .ConfigureAwait(false) == 1;
    }

    public async ValueTask<DarkElfRuinsReward?> RewardAsync(CharacterId character, Guid run, CancellationToken ct) =>
        (await database.ReadAllAsync("SELECT reward_json,claimed,presentation_stage FROM darkelf_ruins_rewards WHERE character_id=@character AND run_id=@run",
            p => { p.AddWithValue("@character", character.Value); p.AddWithValue("@run", run.ToString("N")); },
            r => Decode<RewardData>(r.GetString(0)).ToReward() with { Claimed = r.GetInt32(1) != 0, PresentationStage = r.GetInt32(2) }, ct).ConfigureAwait(false)).SingleOrDefault();

    public async ValueTask<bool> FreezeRewardAsync(CharacterId character, Guid run, DarkElfRuinsReward reward, CancellationToken ct) =>
        await database.ExecuteAsync("INSERT OR IGNORE INTO darkelf_ruins_rewards(character_id,run_id,reward_json,claimed) VALUES(@character,@run,@reward,0)",
            p => { p.AddWithValue("@character", character.Value); p.AddWithValue("@run", run.ToString("N"));
                p.AddWithValue("@reward", JsonSerializer.Serialize(new RewardData(reward.DungeonId, reward.GuardianDefeated,
                    reward.ElapsedMilliseconds, reward.Daily, reward.Clear, reward.Guardian))); }, ct).ConfigureAwait(false) == 1;

    public async ValueTask<int> AdvancePresentationAsync(CharacterId character, Guid run, CancellationToken ct)
    {
        await database.ExecuteAsync("UPDATE darkelf_ruins_rewards SET presentation_stage=MIN(2,presentation_stage+1) WHERE character_id=@character AND run_id=@run",
            p => { p.AddWithValue("@character", character.Value); p.AddWithValue("@run", run.ToString("N")); }, ct).ConfigureAwait(false);
        return (await RewardAsync(character, run, ct).ConfigureAwait(false))?.PresentationStage ?? 0;
    }

    public async ValueTask<bool> ClaimRewardAsync(CharacterId character, Guid run, CancellationToken ct) =>
        await database.ExecuteAsync("UPDATE darkelf_ruins_rewards SET claimed=1 WHERE character_id=@character AND run_id=@run AND claimed=0",
            p => { p.AddWithValue("@character", character.Value); p.AddWithValue("@run", run.ToString("N")); }, ct).ConfigureAwait(false) == 1;

    public async ValueTask<IReadOnlyList<Guid>> PendingRewardsAsync(CharacterId character, CancellationToken ct) =>
        await database.ReadAllAsync("SELECT run_id FROM darkelf_ruins_rewards WHERE character_id=@character AND claimed=0 ORDER BY run_id",
            p => p.AddWithValue("@character", character.Value), r => Guid.ParseExact(r.GetString(0), "N"), ct).ConfigureAwait(false);

    private static T Decode<T>(string json) => JsonSerializer.Deserialize<T>(json)
        ?? throw new InvalidDataException("Ruins state is missing.");
    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    private sealed record RewardData(int DungeonId, bool GuardianDefeated, int ElapsedMilliseconds,
        Dfo.Server.Abstractions.Inventory.ItemDeliveryGrant Daily, Dfo.Server.Abstractions.Inventory.ItemDeliveryGrant[] Clear,
        Dfo.Server.Abstractions.Inventory.ItemDeliveryGrant[] Guardian)
    {
        public DarkElfRuinsReward ToReward() => new(DungeonId, GuardianDefeated, ElapsedMilliseconds, Daily, Clear, Guardian);
    }
}
