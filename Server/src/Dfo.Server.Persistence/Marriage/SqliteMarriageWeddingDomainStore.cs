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
    public ValueTask<IReadOnlyList<MarriageWedding>> PendingAsync(CancellationToken ct) => ReadCoreAsync("phase=0 OR EXISTS(SELECT 1 FROM marriage_wedding_cleanup c WHERE c.ceremony_id=marriage_weddings.ceremony_id) OR EXISTS(SELECT 1 FROM marriage_church_rooms r WHERE r.ceremony_id=marriage_weddings.ceremony_id)", "", ct);
    private ValueTask<IReadOnlyList<MarriageWedding>> ReadCoreAsync(string predicate, string id, CancellationToken ct) =>
        database.ReadAllAsync($"SELECT payload_json,phase,paid_gold,completed_at,love_points FROM marriage_weddings WHERE {predicate}",
            p => p.AddWithValue("@id", id), r => new MarriageWedding(
                JsonSerializer.Deserialize<MarriageCeremony>(r.GetString(0)) ?? throw new InvalidDataException("Invalid wedding."),
                (MarriageWeddingPhase)r.GetInt32(1), r.GetInt32(2))
                { CompletedAt = r.GetInt64(3) == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(3)), LovePoints = r.GetInt32(4) }, ct);
    public async ValueTask CreateAsync(MarriageWedding wedding, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO marriage_weddings(ceremony_id,relation_id,payload_json,phase,paid_gold)
            VALUES(@id,@relation,@payload,0,@gold)
            """, p => { p.AddWithValue("@id", wedding.Ceremony.Id.ToString("N"));
                p.AddWithValue("@relation", wedding.Ceremony.RelationId.ToString("N"));
                p.AddWithValue("@payload", JsonSerializer.Serialize(wedding.Ceremony)); p.AddWithValue("@gold", wedding.PaidGold); }, ct).ConfigureAwait(false);
    public async ValueTask<bool> FinishAsync(Guid ceremony, MarriageWeddingPhase phase, DateTimeOffset at, CancellationToken ct) =>
        await database.ExecuteAsync("UPDATE marriage_weddings SET phase=@phase,completed_at=@at WHERE ceremony_id=@id AND phase=0",
            p => { p.AddWithValue("@id", ceremony.ToString("N")); p.AddWithValue("@phase", (int)phase); p.AddWithValue("@at", phase == MarriageWeddingPhase.Completed ? at.ToUnixTimeMilliseconds() : 0); }, ct).ConfigureAwait(false) == 1;
    public async ValueTask<MarriageWedding?> MarriedAsync(Guid relation, CancellationToken ct) =>
        (await ReadCoreAsync("relation_id=@id AND phase=1", relation.ToString("N"), ct).ConfigureAwait(false)).SingleOrDefault();
    public async ValueTask<bool> AddLovePointsAsync(Guid relation, int amount, int maximum, CancellationToken ct) =>
        amount > 0 && amount <= maximum && await database.ExecuteAsync("UPDATE marriage_weddings SET love_points=love_points+@amount WHERE relation_id=@id AND phase=1 AND love_points<=@maximum-@amount",
            p => { p.AddWithValue("@id", relation.ToString("N")); p.AddWithValue("@amount", amount); p.AddWithValue("@maximum", maximum); }, ct).ConfigureAwait(false) == 1;
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
    public async ValueTask QueueCleanupAsync(Guid ceremony, MarriageWeddingCleanup cleanup, CancellationToken ct) =>
        _ = await database.ExecuteAsync("INSERT OR IGNORE INTO marriage_wedding_cleanup VALUES(@id,@character,@payload)",
            p => { p.AddWithValue("@id", ceremony.ToString("N")); p.AddWithValue("@character", cleanup.Character.Value);
                p.AddWithValue("@payload", JsonSerializer.Serialize(cleanup)); }, ct).ConfigureAwait(false);
    public ValueTask<IReadOnlyList<MarriageWeddingCleanup>> CleanupAsync(Guid ceremony, CancellationToken ct) =>
        database.ReadAllAsync("SELECT payload_json FROM marriage_wedding_cleanup WHERE ceremony_id=@id", p => p.AddWithValue("@id", ceremony.ToString("N")),
            r => JsonSerializer.Deserialize<MarriageWeddingCleanup>(r.GetString(0)) ?? throw new InvalidDataException("Invalid wedding cleanup."), ct);
    public async ValueTask CompleteCleanupAsync(Guid ceremony, CharacterId character, CancellationToken ct) =>
        _ = await database.ExecuteAsync("DELETE FROM marriage_wedding_cleanup WHERE ceremony_id=@id AND character_id=@character",
            p => { p.AddWithValue("@id", ceremony.ToString("N")); p.AddWithValue("@character", character.Value); }, ct).ConfigureAwait(false);
    public async ValueTask<bool> PayAsync(Guid ceremony, CharacterId guest, int gold, CancellationToken ct) =>
        await database.ExecuteAsync("UPDATE marriage_wedding_guests SET gold=@gold WHERE ceremony_id=@id AND guest_character_id=@guest AND gold=0",
            p => { p.AddWithValue("@id", ceremony.ToString("N")); p.AddWithValue("@guest", guest.Value); p.AddWithValue("@gold", gold); }, ct).ConfigureAwait(false) == 1;
}
