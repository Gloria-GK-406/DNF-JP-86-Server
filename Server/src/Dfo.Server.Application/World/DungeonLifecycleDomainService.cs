using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands.Pipeline;
using Dfo.Server.Application.Transactions;

namespace Dfo.Server.Application.World;

internal sealed class DungeonLifecycleDomainService(IEventService events, DomainTransactionScope transactions,
    TransactionalExecutionGate gate) : IDungeonLifecycleDomainService
{
    public async ValueTask RecordDepartureAsync(DungeonParticipantLeft fact, CancellationToken cancellationToken = default)
    {
        if (transactions.Current is not null)
        {
            await events.EnqueueAsync(fact, cancellationToken).ConfigureAwait(false);
            return;
        }
        // Runtime departures also happen on disconnect, outside a command transaction.
        await using var admission = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = transactions.Begin();
        await events.EnqueueAsync(fact, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
