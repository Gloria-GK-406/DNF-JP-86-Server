using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Marriage;

internal sealed class SqliteMarriageChurchDomainStore(SqliteDatabase database) : IMarriageChurchDomainStore
{
    public async ValueTask<MarriageChurchRoom?> ReadAsync(Guid ceremonyId, CancellationToken ct) =>
        (await database.ReadAllAsync("""
            SELECT token,channel_id,town_id,area_id,prepared_at,ready_until FROM marriage_church_rooms
            WHERE ceremony_id=@id
            """, p => p.AddWithValue("@id", ceremonyId.ToString("N")), r => new MarriageChurchRoom(
                checked((uint)r.GetInt64(0)), ceremonyId, r.GetInt32(1), checked((byte)r.GetInt32(2)),
                checked((byte)r.GetInt32(3)), DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(4)),
                DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(5))), ct).ConfigureAwait(false)).SingleOrDefault();

    public async ValueTask<MarriageChurchRoom?> TryCreateAsync(Guid ceremonyId, int channelId,
        MarriageChurchLocation location, DateTimeOffset at, DateTimeOffset until, CancellationToken ct)
    {
        _ = await database.ExecuteAsync("""
            INSERT INTO marriage_church_rooms(ceremony_id,channel_id,town_id,area_id,prepared_at,ready_until)
            VALUES(@id,@channel,@town,@area,@at,@until) ON CONFLICT DO NOTHING
            """, p =>
        {
            p.AddWithValue("@id", ceremonyId.ToString("N")); p.AddWithValue("@channel", channelId);
            p.AddWithValue("@town", location.TownId); p.AddWithValue("@area", location.AreaId);
            p.AddWithValue("@at", at.ToUnixTimeMilliseconds()); p.AddWithValue("@until", until.ToUnixTimeMilliseconds());
        }, ct).ConfigureAwait(false);
        return await ReadAsync(ceremonyId, ct).ConfigureAwait(false);
    }

    public async ValueTask ReleaseAsync(Guid ceremony, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            DELETE FROM marriage_church_admissions WHERE ceremony_id=@id;
            DELETE FROM marriage_church_rooms WHERE ceremony_id=@id;
            """, p => p.AddWithValue("@id", ceremony.ToString("N")), ct).ConfigureAwait(false);

    public async ValueTask AdmitAsync(Guid ceremonyId, CharacterId character, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO marriage_church_admissions(ceremony_id,character_id) VALUES(@id,@character) ON CONFLICT DO NOTHING
            """, p => { p.AddWithValue("@id", ceremonyId.ToString("N")); p.AddWithValue("@character", character.Value); }, ct)
            .ConfigureAwait(false);

    public async ValueTask<bool> IsAdmittedAsync(Guid ceremonyId, CharacterId character, CancellationToken ct) =>
        (await database.ReadAllAsync("""
            SELECT EXISTS(SELECT 1 FROM marriage_church_admissions WHERE ceremony_id=@id AND character_id=@character)
            """, p => { p.AddWithValue("@id", ceremonyId.ToString("N")); p.AddWithValue("@character", character.Value); },
                r => r.GetInt32(0) != 0, ct).ConfigureAwait(false)).Single();
}
