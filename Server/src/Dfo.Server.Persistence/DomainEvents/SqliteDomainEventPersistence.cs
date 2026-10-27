using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.DomainEvents;

internal sealed class SqliteDomainEventPersistence(SqliteDatabase database) : IDomainEventPersistence
{
    public async ValueTask AppendAsync(StoredDomainEvent item,
        IReadOnlyList<DomainEventDeliveryTarget> subscribers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(subscribers);
        await database.ExecuteAsync("""
            INSERT INTO domain_events(event_id,batch_id,occurred_at,origin_instance_id,event_type,schema_version,payload_json)
            VALUES(@event,@batch,@at,@origin,@type,@version,@json)
            """, p =>
        {
            p.AddWithValue("@event", Id(item.EventId)); p.AddWithValue("@batch", Id(item.BatchId));
            p.AddWithValue("@at", item.OccurredAt.ToUnixTimeMilliseconds());
            p.AddWithValue("@origin", Id(item.OriginInstanceId)); p.AddWithValue("@type", item.EventType);
            p.AddWithValue("@version", item.SchemaVersion); p.AddWithValue("@json", item.PayloadJson);
        }, cancellationToken).ConfigureAwait(false);
        foreach (var subscriber in subscribers)
            await database.ExecuteAsync("""
                INSERT INTO domain_event_deliveries(event_id,subscriber_id,coalescing_key,next_attempt_at)
                VALUES(@event,@subscriber,@key,@due)
                """, p =>
            {
                Identify(p, item.EventId, subscriber.SubscriberId);
                p.AddWithValue("@key", (object?)subscriber.CoalescingKey ?? DBNull.Value);
                p.AddWithValue("@due", item.OccurredAt.ToUnixTimeMilliseconds());
            }, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<PendingDomainEventDelivery>> ReadDueAsync(string subscriberId,
        DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return database.ReadAllAsync("""
            SELECT e.event_id,e.batch_id,e.occurred_at,e.origin_instance_id,e.event_type,e.schema_version,e.payload_json,
                   d.subscriber_id,d.coalescing_key,d.attempt_count
            FROM domain_event_deliveries d JOIN domain_events e ON e.event_id=d.event_id
            WHERE d.subscriber_id=@subscriber AND d.state=0 AND d.next_attempt_at<=@now
            ORDER BY e.sequence LIMIT @limit
            """, p =>
        {
            p.AddWithValue("@subscriber", subscriberId); p.AddWithValue("@now", now.ToUnixTimeMilliseconds());
            p.AddWithValue("@limit", limit);
        }, r => new PendingDomainEventDelivery(new StoredDomainEvent(Guid.Parse(r.GetString(0)),
            Guid.Parse(r.GetString(1)), DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(2)),
            Guid.Parse(r.GetString(3)), r.GetString(4), r.GetInt32(5), r.GetString(6)),
            r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetInt32(9)), cancellationToken);
    }

    public async ValueTask<DateTimeOffset?> NextDueAsync(string subscriberId, CancellationToken cancellationToken)
    {
        var row = await database.ReadFirstAsync("""
            SELECT MIN(next_attempt_at) FROM domain_event_deliveries WHERE subscriber_id=@subscriber AND state=0
            """, p => p.AddWithValue("@subscriber", subscriberId),
            r => new Due(r.IsDBNull(0) ? null : r.GetInt64(0)), cancellationToken).ConfigureAwait(false);
        return row?.At is { } at ? DateTimeOffset.FromUnixTimeMilliseconds(at) : null;
    }

    public async ValueTask<bool> TryBeginAsync(Guid eventId, string subscriberId, DateTimeOffset now,
        CancellationToken cancellationToken) => await database.ExecuteAsync("""
            UPDATE domain_event_deliveries SET attempt_count=attempt_count+1
            WHERE event_id=@event AND subscriber_id=@subscriber AND state=0 AND next_attempt_at<=@now
            """, p => { Identify(p, eventId, subscriberId); p.AddWithValue("@now", now.ToUnixTimeMilliseconds()); },
            cancellationToken).ConfigureAwait(false) == 1;

    public async ValueTask<bool> HasCompletedCoalescingKeyAsync(Guid batchId, string subscriberId, string key,
        CancellationToken cancellationToken) => await database.ReadFirstAsync("""
            SELECT 1 FROM domain_event_deliveries d JOIN domain_events e ON e.event_id=d.event_id
            WHERE e.batch_id=@batch AND d.subscriber_id=@subscriber AND d.coalescing_key=@key AND d.state=1 LIMIT 1
            """, p =>
        {
            p.AddWithValue("@batch", Id(batchId)); p.AddWithValue("@subscriber", subscriberId); p.AddWithValue("@key", key);
        }, _ => new Present(), cancellationToken).ConfigureAwait(false) is not null;

    public async ValueTask CompleteAsync(Guid eventId, string subscriberId, DateTimeOffset now, string reason,
        CancellationToken cancellationToken)
    {
        var touched = await database.ExecuteAsync("""
            UPDATE domain_event_deliveries SET state=1,processed_at=@now,last_error=@reason
            WHERE event_id=@event AND subscriber_id=@subscriber AND state=0
            """, p =>
        {
            Identify(p, eventId, subscriberId); p.AddWithValue("@now", now.ToUnixTimeMilliseconds());
            p.AddWithValue("@reason", reason);
        }, cancellationToken).ConfigureAwait(false);
        if (touched != 1) throw new InvalidOperationException("Domain event delivery is no longer pending.");
    }

    public async ValueTask<bool> QuarantineAsync(Guid eventId, string subscriberId, int expectedAttemptCount,
        string error, CancellationToken cancellationToken) => await database.ExecuteAsync("""
            UPDATE domain_event_deliveries SET state=2,attempt_count=MAX(attempt_count,@expected+1),last_error=@error
            WHERE event_id=@event AND subscriber_id=@subscriber AND state=0 AND attempt_count>=@expected
            """, p =>
        {
            Identify(p, eventId, subscriberId); p.AddWithValue("@expected", expectedAttemptCount);
            p.AddWithValue("@error", error);
        }, cancellationToken).ConfigureAwait(false) == 1;

    public async ValueTask<bool> TryFailAsync(Guid eventId, string subscriberId, int expectedAttemptCount,
        DateTimeOffset nextAttemptAt, string error, bool terminal, CancellationToken cancellationToken) =>
        await database.ExecuteAsync("""
            UPDATE domain_event_deliveries SET state=@state,attempt_count=attempt_count+1,
                next_attempt_at=@due,last_error=@error
            WHERE event_id=@event AND subscriber_id=@subscriber AND state=0 AND attempt_count=@expected
            """, p =>
        {
            Identify(p, eventId, subscriberId); p.AddWithValue("@expected", expectedAttemptCount);
            p.AddWithValue("@state", terminal ? 2 : 0); p.AddWithValue("@due", nextAttemptAt.ToUnixTimeMilliseconds());
            p.AddWithValue("@error", error);
        }, cancellationToken).ConfigureAwait(false) == 1;

    public async ValueTask FailAsync(Guid eventId, string subscriberId, DateTimeOffset nextAttemptAt,
        string error, bool terminal, CancellationToken cancellationToken)
    {
        await database.ExecuteAsync("""
            UPDATE domain_event_deliveries SET state=@state,attempt_count=attempt_count+1,
                next_attempt_at=@due,last_error=@error
            WHERE event_id=@event AND subscriber_id=@subscriber AND state=0
            """, p =>
        {
            Identify(p, eventId, subscriberId); p.AddWithValue("@state", terminal ? 2 : 0);
            p.AddWithValue("@due", nextAttemptAt.ToUnixTimeMilliseconds()); p.AddWithValue("@error", error);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string Id(Guid id) => id.ToString("N");
    private static void Identify(SqliteParameterCollection p, Guid eventId, string subscriberId)
    { p.AddWithValue("@event", Id(eventId)); p.AddWithValue("@subscriber", subscriberId); }
    private sealed record Due(long? At);
    private sealed record Present;
}
