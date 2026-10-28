using System.Text.Json;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

internal sealed class SqliteGuardianAltarProgressDomainStore(SqliteDatabase database) : IGuardianAltarProgressDomainStore
{
    public async ValueTask<GuardianAltarProgress?> ReadAsync(CharacterId character, CancellationToken ct) =>
        (await database.ReadAllAsync("SELECT state_json FROM character_guardian_altar WHERE character_id=@id",
            p => p.AddWithValue("@id", character.Value), r => JsonSerializer.Deserialize<GuardianAltarProgress>(r.GetString(0))
                ?? throw new InvalidDataException("Invalid guardian altar progress."), ct).ConfigureAwait(false)).SingleOrDefault();

    public async ValueTask SaveAsync(CharacterId character, GuardianAltarProgress value, long? expectedRevision, CancellationToken ct)
    {
        if (value.Revision != (expectedRevision is null ? 0 : checked(expectedRevision.Value + 1)))
            throw new InvalidOperationException("Guardian altar progress revision must advance once.");
        var sql = expectedRevision is null
            ? "INSERT INTO character_guardian_altar(character_id,revision,state_json) VALUES(@id,@next,@json) ON CONFLICT DO NOTHING"
            : "UPDATE character_guardian_altar SET revision=@next,state_json=@json WHERE character_id=@id AND revision=@old";
        var changed = await database.ExecuteAsync(sql, p =>
        {
            p.AddWithValue("@id", character.Value); p.AddWithValue("@next", value.Revision);
            p.AddWithValue("@json", JsonSerializer.Serialize(value));
            if (expectedRevision is not null) p.AddWithValue("@old", expectedRevision.Value);
        }, ct).ConfigureAwait(false);
        if (changed != 1) throw new InvalidOperationException("Guardian altar progress changed before settlement.");
    }
    public async ValueTask<bool> TryRecordSettlementAsync(CharacterId character, Guid runId, int round, CancellationToken ct)
    {
        if (runId == Guid.Empty || round < 0) throw new ArgumentOutOfRangeException(nameof(round));
        return await database.ExecuteAsync("""
            INSERT INTO guardian_altar_settlements(character_id,run_id,round) VALUES(@id,@run,@round)
            ON CONFLICT DO NOTHING
            """, p => { p.AddWithValue("@id", character.Value); p.AddWithValue("@run", runId.ToString("N"));
                p.AddWithValue("@round", round); }, ct).ConfigureAwait(false) == 1;
    }
}
