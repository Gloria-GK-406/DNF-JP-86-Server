using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Transactions;

namespace Dfo.Server.Application.World;

internal sealed class DungeonLifecycleDomainService(IEventService events, DomainTransactionScope transactions)
    : IDungeonLifecycleDomainService
{
    public async ValueTask RecordDepartureAsync(DungeonParticipantLeft fact, CancellationToken cancellationToken = default)
    {
        // The event commits with the departure: a command's unit, or the disconnect cleanup's own step.
        if (transactions.Current is null)
            throw new InvalidOperationException("Recording a dungeon departure requires the caller's transaction.");
        await events.EnqueueAsync(fact, cancellationToken).ConfigureAwait(false);
    }
}
