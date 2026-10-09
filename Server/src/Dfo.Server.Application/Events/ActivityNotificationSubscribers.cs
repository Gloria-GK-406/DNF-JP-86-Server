using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;

namespace Dfo.Server.Application.Events;

internal sealed class JoustLifecycleNotificationSubscriber(IEventService events, ISelectedSessionSnapshotQuery online)
    : IDomainEventSubscriber<JoustLifecycleChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<JoustLifecycleChanged> message, CancellationToken cancellationToken)
    {
        var kind = message.Payload.Transition switch
        {
            JoustLifecycleTransition.RoundStarted => JoustLifecycleNotificationKind.RoundStarted,
            JoustLifecycleTransition.PhaseChanged => JoustLifecycleNotificationKind.PhaseChanged,
            JoustLifecycleTransition.RoundEnded => JoustLifecycleNotificationKind.RoundEnded,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        await events.ProjectAsync(new NotificationAudience.TheseSessions(online.Snapshot()),
            new JoustLifecycleNotification(message.Payload.State, kind), cancellationToken);
    }
}

internal sealed class EventsConfigurationNotificationSubscriber(IEventService events, ISelectedSessionSnapshotQuery online)
    : IDomainEventSubscriber<ManagedEventConfigurationChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<ManagedEventConfigurationChanged> message, CancellationToken cancellationToken) =>
        await events.ProjectAsync(new NotificationAudience.TheseSessions(online.Snapshot()), new EventsChanged(), cancellationToken);
}
