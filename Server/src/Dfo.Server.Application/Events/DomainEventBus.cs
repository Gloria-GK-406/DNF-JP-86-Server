using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Events;

internal sealed class DomainEventBus(IServiceScopeFactory scopes, IDomainTransactionAccessor transactions,
    ILogger<DomainEventBus> logger) : IDomainEventBus
{
    public async ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : class, IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (transactions.Current is not null)
            throw new InvalidOperationException("Domain events require a completed operation; queries and open transactions cannot publish.");
        await using var scope = scopes.CreateAsyncScope();
        foreach (var subscriber in scope.ServiceProvider.GetServices<IDomainEventHandler<TEvent>>())
        {
            try
            {
                await subscriber.HandleAsync(domainEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Committed domain event {Event} subscriber {Subscriber} failed; no replay.",
                    typeof(TEvent).Name, subscriber.GetType().Name);
            }
        }
    }
}
