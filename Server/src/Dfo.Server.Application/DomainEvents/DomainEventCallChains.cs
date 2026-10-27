using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;

namespace Dfo.Server.Application.DomainEvents;

/// <summary>
/// RQ-051: the chain of the CMD that published each delivery, in process memory only; the tables do not change.
/// Recorded with the event rows, so a delivery the dispatcher can read always has its chain already; forgotten
/// when the delivery is settled, or when the publishing transaction rolls back. A delivery left by another
/// process has none and is consumed as a startup recovery root.
/// </summary>
/// <remarks>
/// Call-chain metadata, not gameplay state: written on the executor, read by the dispatcher off it. A commit whose
/// outcome is uncertain neither commits nor rolls back its batch; what it recorded is then never read and stays
/// until the process ends, at most one entry per fact and subscriber of that transaction.
/// </remarks>
internal sealed class DomainEventCallChains
{
    private readonly ConcurrentDictionary<(Guid EventId, string SubscriberId), GameplayCallChain> chains = new();

    internal int Count => chains.Count;

    public void Record(Guid eventId, string subscriberId, GameplayCallChain publisher) =>
        chains.TryAdd((eventId, subscriberId), publisher);

    public bool TryGet(Guid eventId, string subscriberId, out GameplayCallChain publisher) =>
        chains.TryGetValue((eventId, subscriberId), out publisher);

    public void Forget(Guid eventId, string subscriberId) => chains.TryRemove((eventId, subscriberId), out _);
}
