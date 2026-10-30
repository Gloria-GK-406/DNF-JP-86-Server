using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Application.Events;

internal sealed class ManagedEventLifecycleDomainService(IEnumerable<IManagedEventDomainService> registered,
    IManagedEventActivationPersistence store, IEventService facts, IDomainTransactionAccessor transactions)
    : IManagedEventLifecycleDomainService
{
    private readonly IReadOnlyDictionary<int, IManagedEventDomainService> activities = registered.ToDictionary(e => e.EventId);
    public IReadOnlyList<int> EventIds => activities.Keys.Order().ToArray();
    public ValueTask<ManagedEventActivationState?> AppliedAsync(int eventId, CancellationToken ct) => store.ReadAsync(eventId, ct);

    public async ValueTask<bool> ReconcileAsync(int eventId, DateTimeOffset now, CancellationToken ct, bool notifyClients = true)
    {
        if (transactions.Current is null || transactions.Current.IsReadOnly)
            throw new InvalidOperationException("Activity reconciliation requires a writable command transaction.");
        var activity = activities[eventId];
        var description = await activity.DescribeAsync(now, ct).ConfigureAwait(false);
        var active = await activity.IsActiveAsync(now, ct).ConfigureAwait(false);
        var previous = await store.ReadAsync(eventId, ct).ConfigureAwait(false);
        if (previous is not null && previous.Active == active && previous.ConfigurationRevision == description.Revision)
            return false;
        var change = new ManagedEventActivationChanged(eventId, active, description.Revision, now);
        await facts.DirectAsync(change, ct).ConfigureAwait(false);
        await store.SaveAsync(eventId, new(active, description.Revision, now), ct).ConfigureAwait(false);
        // Configuration edits already enqueue their own refresh; clock transitions need this one.
        if (notifyClients && previous?.Active != active && (previous is not null || active))
            await facts.EnqueueAsync(change, ct).ConfigureAwait(false);
        return true;
    }
}
