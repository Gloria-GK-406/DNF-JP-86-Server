using System.Text.Json;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Marriage;

internal sealed class SqliteMarriageWeddingDomainStore(SqliteDatabase database) : IMarriageWeddingDomainStore
{
    public async ValueTask<MarriageWedding?> ReadAsync(Guid ceremony, CancellationToken ct) =>
        (await ReadCoreAsync("ceremony_id=@id", ceremony.ToString("N"), ct).ConfigureAwait(false)).SingleOrDefault();
    public ValueTask<IReadOnlyList<MarriageWedding>> PendingAsync(CancellationToken ct) => ReadCoreAsync("phase=0 OR EXISTS(SELECT 1 FROM marriage_church_rooms r WHERE r.ceremony_id=marriage_weddings.ceremony_id)", "", ct);
    private ValueTask<IReadOnlyList<MarriageWedding>> ReadCoreAsync(string predicate, string id, CancellationToken ct) =>
        database.ReadAllAsync($"SELECT payload_json,phase,paid_gold FROM marriage_weddings WHERE {predicate}",
            p => p.AddWithValue("@id", id), r => new MarriageWedding(
                JsonSerializer.Deserialize<MarriageCeremony>(r.GetString(0)) ?? throw new InvalidDataException("Invalid wedding."),
                (MarriageWeddingPhase)r.GetInt32(1), r.GetInt32(2)), ct);
    public async ValueTask CreateAsync(MarriageWedding wedding, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO marriage_weddings(ceremony_id,relation_id,payload_json,phase,paid_gold)
            VALUES(@id,@relation,@payload,0,@gold)
            """, p => { p.AddWithValue("@id", wedding.Ceremony.Id.ToString("N"));
                p.AddWithValue("@relation", wedding.Ceremony.RelationId.ToString("N"));
                p.AddWithValue("@payload", JsonSerializer.Serialize(wedding.Ceremony)); p.AddWithValue("@gold", wedding.PaidGold); }, ct).ConfigureAwait(false);
    public async ValueTask<bool> FinishAsync(Guid ceremony, MarriageWeddingPhase phase, CancellationToken ct) =>
        await database.ExecuteAsync("UPDATE marriage_weddings SET phase=@phase WHERE ceremony_id=@id AND phase=0",
            p => { p.AddWithValue("@id", ceremony.ToString("N")); p.AddWithValue("@phase", (int)phase); }, ct).ConfigureAwait(false) == 1;
    public async ValueTask<bool> IsMarriedAsync(Guid relation, CancellationToken ct) =>
        (await database.ReadAllAsync("SELECT EXISTS(SELECT 1 FROM marriage_weddings WHERE relation_id=@id AND phase=1)",
            p => p.AddWithValue("@id", relation.ToString("N")), r => r.GetInt32(0) != 0, ct).ConfigureAwait(false)).Single();
    public ValueTask<IReadOnlyList<MarriageWeddingInvitation>> InvitationsAsync(Guid ceremony, CancellationToken ct) =>
        database.ReadAllAsync("SELECT guest_character_id,inviter_character_id,context_key,gold FROM marriage_wedding_guests WHERE ceremony_id=@id",
            p => p.AddWithValue("@id", ceremony.ToString("N")), r => new MarriageWeddingInvitation(ceremony,
                new(r.GetInt32(0)), new(r.GetInt32(1)), checked((uint)r.GetInt64(2)), r.GetInt32(3)), ct);
    public async ValueTask InviteAsync(MarriageWeddingInvitation invitation, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO marriage_wedding_guests(ceremony_id,guest_character_id,inviter_character_id,context_key,gold)
            VALUES(@id,@guest,@inviter,@key,0)
            """, p => { p.AddWithValue("@id", invitation.CeremonyId.ToString("N")); p.AddWithValue("@guest", invitation.Guest.Value);
                p.AddWithValue("@inviter", invitation.Inviter.Value); p.AddWithValue("@key", invitation.Key); }, ct).ConfigureAwait(false);
    public async ValueTask<bool> PayAsync(Guid ceremony, CharacterId guest, int gold, CancellationToken ct) =>
        await database.ExecuteAsync("UPDATE marriage_wedding_guests SET gold=@gold WHERE ceremony_id=@id AND guest_character_id=@guest AND gold=0",
            p => { p.AddWithValue("@id", ceremony.ToString("N")); p.AddWithValue("@guest", guest.Value); p.AddWithValue("@gold", gold); }, ct).ConfigureAwait(false) == 1;
}
