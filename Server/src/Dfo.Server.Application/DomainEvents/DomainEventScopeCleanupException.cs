using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.DomainEvents;
internal sealed class DomainEventScopeCleanupException(Exception cause) : Exception("An immediate event scope could not be released.", cause)
{
    internal static bool Contains(Exception error) => error is DomainEventScopeCleanupException
        || error is AggregateException aggregate && aggregate.InnerExceptions.Any(Contains)
        || error.InnerException is {} inner && Contains(inner);
    internal static async ValueTask DisposeAsync(AsyncServiceScope scope, Exception? invocation)
    {
        try { await scope.DisposeAsync().ConfigureAwait(false); }
        catch (Exception cleanup)
        {
            throw new DomainEventScopeCleanupException(invocation is null ? cleanup
                : new AggregateException("The event invocation and its scope cleanup both failed.", invocation, cleanup));
        }
    }
}
