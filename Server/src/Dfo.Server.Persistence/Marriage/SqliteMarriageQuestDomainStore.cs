using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Marriage;

internal sealed class SqliteMarriageQuestDomainStore(SqliteDatabase database) : IMarriageQuestDomainStore
{
    public async ValueTask<MarriageQuestBinding?> ReadAsync(CharacterId character, int quest, CancellationToken ct) =>
        (await database.ReadAllAsync("SELECT proposal_id,accepted_at FROM marriage_quest_bindings WHERE character_id=@character AND quest_id=@quest",
            p => { p.AddWithValue("@character", character.Value); p.AddWithValue("@quest", quest); },
            r => new MarriageQuestBinding(Guid.Parse(r.GetString(0)), DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(1))), ct).ConfigureAwait(false)).SingleOrDefault();

    public async ValueTask BindAsync(CharacterId character, int quest, Guid relation, DateTimeOffset at, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            DELETE FROM marriage_trial_clears WHERE character_id=@character AND quest_id=@quest;
            INSERT INTO marriage_quest_bindings(character_id,quest_id,proposal_id,accepted_at) VALUES(@character,@quest,@relation,@at)
            ON CONFLICT(character_id,quest_id) DO UPDATE SET proposal_id=excluded.proposal_id,accepted_at=excluded.accepted_at;
            """, p => { p.AddWithValue("@character", character.Value); p.AddWithValue("@quest", quest);
                p.AddWithValue("@relation", relation.ToString("N")); p.AddWithValue("@at", at.ToUnixTimeMilliseconds()); }, ct).ConfigureAwait(false);

    public async ValueTask<bool> RecordClearAsync(CharacterId character, int quest, Guid relation, DungeonRunId run, CancellationToken ct) =>
        await database.ExecuteAsync("INSERT OR IGNORE INTO marriage_trial_clears(character_id,quest_id,proposal_id,run_id) VALUES(@character,@quest,@relation,@run)",
            p => { p.AddWithValue("@character", character.Value); p.AddWithValue("@quest", quest);
                p.AddWithValue("@relation", relation.ToString("N")); p.AddWithValue("@run", run.Value.ToString("N")); }, ct).ConfigureAwait(false) == 1;

    public async ValueTask<int> CountClearsAsync(CharacterId character, int quest, Guid relation, CancellationToken ct) =>
        (await database.ReadAllAsync("SELECT COUNT(*) FROM marriage_trial_clears WHERE character_id=@character AND quest_id=@quest AND proposal_id=@relation",
            p => { p.AddWithValue("@character", character.Value); p.AddWithValue("@quest", quest); p.AddWithValue("@relation", relation.ToString("N")); },
            r => r.GetInt32(0), ct).ConfigureAwait(false)).Single();
}
