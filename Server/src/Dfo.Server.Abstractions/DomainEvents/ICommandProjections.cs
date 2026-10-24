using Dfo.Server.Abstractions.Notifications;

namespace Dfo.Server.Abstractions.DomainEvents;

public interface ICommandProjections
{
    ValueTask<NotificationDelivery> ProjectAsync<TNotification>(NotificationAudience audience,
        TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : class, INotification<TNotification>;
}
