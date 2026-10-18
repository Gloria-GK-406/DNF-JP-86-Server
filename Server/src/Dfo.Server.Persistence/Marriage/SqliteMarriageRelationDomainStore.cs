using System.Text.Json;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Marriage;

internal sealed class SqliteMarriageRelationDomainStore(SqliteDatabase database) : IMarriageRelationDomainStore
{
    public async ValueTask<int> PointsAsync(Guid relation, CharacterId character, CancellationToken ct) =>
        (await database.ReadAllAsync("SELECT points FROM marriage_personal_points WHERE relation_id=@id AND character_id=@char", p => { p.AddWithValue("@id", relation.ToString("N")); p.AddWithValue("@char", character.Value); }, r => r.GetInt32(0), ct).ConfigureAwait(false)).SingleOrDefault();

    public async ValueTask<bool> ChangePointsAsync(Guid relation, CharacterId character, int amount, CancellationToken ct)
    {
        var id = relation.ToString("N");
        _ = await database.ExecuteAsync("""
            INSERT OR IGNORE INTO marriage_personal_points(relation_id,character_id,points)
            SELECT @id,@char,0 WHERE EXISTS(SELECT 1 FROM marriage_proposal_participants p JOIN marriage_relations r ON r.id=p.proposal_id
                WHERE p.proposal_id=@id AND p.character_id=@char AND p.active=1 AND r.stage=4)
            """, p => { p.AddWithValue("@id", id); p.AddWithValue("@char", character.Value); }, ct).ConfigureAwait(false);
        return await database.ExecuteAsync("""
            UPDATE marriage_personal_points SET points=points+@amount,revision=revision+1
            WHERE relation_id=@id AND character_id=@char AND points+@amount BETWEEN 0 AND 999
                AND EXISTS(SELECT 1 FROM marriage_relations WHERE id=@id AND stage=4)
            """, p => { p.AddWithValue("@id", id); p.AddWithValue("@char", character.Value); p.AddWithValue("@amount", amount); }, ct).ConfigureAwait(false) == 1;
    }
    public async ValueTask<bool> DailyPointAsync(Guid relation, CharacterId character, long period, CancellationToken ct)
    {
        if (await database.ExecuteAsync("INSERT OR IGNORE INTO marriage_daily_points VALUES(@char,@period)", p => { p.AddWithValue("@char", character.Value); p.AddWithValue("@period", period); }, ct).ConfigureAwait(false) != 1) return false;
        _ = await ChangePointsAsync(relation, character, 0, ct).ConfigureAwait(false);
        _ = await database.ExecuteAsync("UPDATE marriage_personal_points SET points=MIN(999,points+1),revision=revision+1 WHERE relation_id=@id AND character_id=@char", p => { p.AddWithValue("@id", relation.ToString("N")); p.AddWithValue("@char", character.Value); }, ct).ConfigureAwait(false);
        return true;
    }
    public async ValueTask<byte> RingAsync(Guid relation, CancellationToken ct) =>
        (await database.ReadAllAsync("SELECT level FROM marriage_ring_state WHERE relation_id=@id", p => p.AddWithValue("@id", relation.ToString("N")), r => checked((byte)r.GetInt32(0)), ct).ConfigureAwait(false)).SingleOrDefault();
    public async ValueTask<bool> UpgradeRingAsync(Guid relation, byte expected, CancellationToken ct)
    {
        if (expected >= 5) return false;
        var id = relation.ToString("N");
        _ = await database.ExecuteAsync("INSERT OR IGNORE INTO marriage_ring_state VALUES(@id,0)", p => p.AddWithValue("@id", id), ct).ConfigureAwait(false);
        return await database.ExecuteAsync("UPDATE marriage_ring_state SET level=level+1 WHERE relation_id=@id AND level=@expected", p => { p.AddWithValue("@id", id); p.AddWithValue("@expected", expected); }, ct).ConfigureAwait(false) == 1;
    }
    private const string Select = """
        SELECT r.id,r.stage,r.revision,r.stage_since,r.ceremony_id,r.married_at,r.love_points,p.payload_json
        FROM marriage_relations r JOIN marriage_proposals p ON p.id=r.id
        """;

    public async ValueTask<MarriageRelation?> FindActiveAsync(CharacterId character, CancellationToken ct) =>
        (await ReadCoreAsync(Select + """
             JOIN marriage_proposal_participants a ON a.proposal_id=r.id
            WHERE a.character_id=@value AND a.active=1 ORDER BY p.created_at DESC,p.rowid DESC LIMIT 1
            """, character.Value, ct).ConfigureAwait(false)).SingleOrDefault();

    public async ValueTask<MarriageRelation?> ReadAsync(Guid relation, CancellationToken ct) =>
        (await ReadCoreAsync(Select + " WHERE r.id=@value", relation.ToString("N"), ct).ConfigureAwait(false)).SingleOrDefault();

    private ValueTask<IReadOnlyList<MarriageRelation>> ReadCoreAsync(string statement, object value, CancellationToken ct) =>
        database.ReadAllAsync(statement, p => p.AddWithValue("@value", value), r =>
        {
            var proposal = JsonSerializer.Deserialize<MarriageProposal>(r.GetString(7))
                ?? throw new InvalidDataException("Invalid marriage proposal.");
            return new MarriageRelation(Guid.Parse(r.GetString(0)), (MarriageStage)r.GetInt32(1), r.GetInt64(2),
                proposal.Proposer, proposal.Recipient, r.IsDBNull(4) ? null : Guid.Parse(r.GetString(4)),
                DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(3)))
            {
                MarriedAt = r.IsDBNull(5) ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(5)),
                LovePoints = r.GetInt32(6),
            };
        }, ct);

    public async ValueTask CreateAsync(Guid relation, MarriageStage stage, DateTimeOffset at, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO marriage_relations(id,stage,revision,stage_since) VALUES(@id,@stage,1,@at)
            """, p =>
        {
            p.AddWithValue("@id", relation.ToString("N"));
            p.AddWithValue("@stage", (int)stage);
            p.AddWithValue("@at", at.ToUnixTimeMilliseconds());
        }, ct).ConfigureAwait(false);

    public async ValueTask<bool> TransitionAsync(MarriageRelation expected, MarriageStage stage, Guid? ceremony,
        DateTimeOffset at, DateTimeOffset? marriedAt, CancellationToken ct) => await database.ExecuteAsync("""
            UPDATE marriage_relations SET stage=@stage,revision=revision+1,stage_since=@at,ceremony_id=@ceremony,
                married_at=COALESCE(@married,married_at)
            WHERE id=@id AND stage=@old AND revision=@revision
            """, p =>
        {
            p.AddWithValue("@id", expected.Id.ToString("N"));
            p.AddWithValue("@old", (int)expected.Stage);
            p.AddWithValue("@revision", expected.Revision);
            p.AddWithValue("@stage", (int)stage);
            p.AddWithValue("@at", at.ToUnixTimeMilliseconds());
            p.AddWithValue("@ceremony", ceremony is { } id ? id.ToString("N") : DBNull.Value);
            p.AddWithValue("@married", marriedAt is { } married ? married.ToUnixTimeMilliseconds() : DBNull.Value);
        }, ct).ConfigureAwait(false) == 1;

    public async ValueTask<int> CountUnsettledReservationsAsync(CancellationToken ct) =>
        (await database.ReadAllAsync("""
            SELECT COUNT(*) FROM marriage_relations r JOIN marriage_ceremonies c ON c.relation_id=r.id AND c.state=1
            WHERE r.stage=1 AND NOT EXISTS(SELECT 1 FROM marriage_weddings w WHERE w.ceremony_id=c.id AND w.phase IN (0,1))
            """, _ => { }, r => r.GetInt32(0), ct).ConfigureAwait(false)).Single();
}
