using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
namespace Dfo.Server.Protocol.Game;
internal sealed class LegacyCommandProjections(IEventService events) : ICommandProjections
{
    public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience,
        T notification, CancellationToken cancellationToken = default)
        where T : class, INotification<T> => events.ProjectAsync(audience, notification, cancellationToken);
}
