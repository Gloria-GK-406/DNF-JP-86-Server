using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;

namespace Dfo.Server.Application.Events;

/// <summary>Turns a committed round transition into one broadcast to the current online audience.</summary>
internal sealed class JoustLifecycleEventHandler(INotificationBus notifications, ISelectedSessionSnapshotQuery selected)
    : IDomainEventHandler<JoustLifecycleEvent>
{
    public async ValueTask HandleAsync(JoustLifecycleEvent domainEvent, CancellationToken cancellationToken) =>
        _ = await notifications.PublishAsync(new NotificationAudience.TheseSessions(selected.Snapshot()),
            new JoustLifecycleNotification(domainEvent.State, domainEvent.Kind), cancellationToken);
}
