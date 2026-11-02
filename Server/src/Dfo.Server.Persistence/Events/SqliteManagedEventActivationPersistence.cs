using Dfo.Server.Abstractions.Events;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Events;

internal sealed class SqliteManagedEventActivationPersistence(SqliteDatabase database) : IManagedEventActivationPersistence
{
    public ValueTask<ManagedEventActivationState?> ReadAsync(int eventId, CancellationToken ct) =>
        database.ReadFirstAsync("SELECT active,configuration_revision,applied_at FROM event_activation_state WHERE event_id=@id;",
            p => p.AddWithValue("@id", eventId), r => new ManagedEventActivationState(r.GetBoolean(0), r.GetInt64(1),
                DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(2))), ct);

    public async ValueTask SaveAsync(int eventId, ManagedEventActivationState value, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO event_activation_state(event_id,active,configuration_revision,applied_at) VALUES(@id,@active,@revision,@at)
            ON CONFLICT(event_id) DO UPDATE SET active=excluded.active,configuration_revision=excluded.configuration_revision,applied_at=excluded.applied_at;
            """, p => { p.AddWithValue("@id", eventId); p.AddWithValue("@active", value.Active);
                p.AddWithValue("@revision", value.ConfigurationRevision); p.AddWithValue("@at", value.AppliedAt.ToUnixTimeMilliseconds()); }, ct);
}
