using System.Text.Json;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Marriage;

internal sealed class SqliteMarriageCeremonyDomainStore(SqliteDatabase database)
    : IMarriageCeremonyDomainStore
{
    public async ValueTask<MarriageCeremony?> FindActiveByRelationAsync(Guid relationId, CancellationToken ct) =>
        (await ReadAsync("relation_id=@value AND state IN (0,1)", relationId.ToString("N"), ct)
            .ConfigureAwait(false)).SingleOrDefault();

    public async ValueTask<MarriageCeremony?> FindLatestReceivedAsync(CharacterId character, CancellationToken ct) =>
        (await ReadAsync("partner_character_id=@value", character.Value, ct).ConfigureAwait(false)).SingleOrDefault();

    private ValueTask<IReadOnlyList<MarriageCeremony>> ReadAsync(string predicate, object value, CancellationToken ct) =>
        database.ReadAllAsync($"""
            SELECT payload_json,state,revision,response
            FROM marriage_ceremonies WHERE {predicate}
            ORDER BY created_at DESC,rowid DESC LIMIT 1
            """, p => p.AddWithValue("@value", value), r =>
                (JsonSerializer.Deserialize<MarriageCeremony>(r.GetString(0))
                    ?? throw new InvalidDataException("Invalid marriage ceremony.")) with
                {
                    State = (MarriageCeremonyState)r.GetInt32(1),
                    Revision = r.GetInt64(2),
                    Response = r.IsDBNull(3) ? null : checked((byte)r.GetInt32(3)),
                }, ct);

    public async ValueTask CreateAsync(MarriageCeremony ceremony, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO marriage_ceremonies(
                id,relation_id,partner_character_id,payload_json,tier,wait_index,state,revision,
                created_at,expires_at,response)
            VALUES(@id,@relation,@partnerCharacter,@payload,@tier,@wait,0,1,@created,@expires,NULL)
            """, p =>
        {
            p.AddWithValue("@id", ceremony.Id.ToString("N"));
            p.AddWithValue("@relation", ceremony.RelationId.ToString("N"));
            p.AddWithValue("@partnerCharacter", ceremony.Partner.CharacterId.Value);
            p.AddWithValue("@payload", JsonSerializer.Serialize(ceremony));
            p.AddWithValue("@tier", ceremony.Tier);
            p.AddWithValue("@wait", ceremony.WaitIndex);
            p.AddWithValue("@created", ceremony.CreatedAt.ToUnixTimeMilliseconds());
            p.AddWithValue("@expires", ceremony.ExpiresAt.ToUnixTimeMilliseconds());
        }, ct).ConfigureAwait(false);

    public async ValueTask<bool> TransitionAsync(MarriageCeremony expected, MarriageCeremonyState state,
        byte response, CancellationToken ct) => await database.ExecuteAsync("""
            UPDATE marriage_ceremonies SET state=@state,revision=revision+1,response=@response
            WHERE id=@id AND state=@oldState AND revision=@revision
            """, p =>
        {
            p.AddWithValue("@id", expected.Id.ToString("N"));
            p.AddWithValue("@oldState", (int)expected.State);
            p.AddWithValue("@revision", expected.Revision);
            p.AddWithValue("@state", (int)state);
            p.AddWithValue("@response", response);
        }, ct).ConfigureAwait(false) == 1;
}
