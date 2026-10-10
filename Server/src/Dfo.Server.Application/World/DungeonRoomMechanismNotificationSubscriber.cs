using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class DungeonRoomMechanismNotificationSubscriber(IEventService events,
    ISelectedSessionSnapshotQuery online) : IDomainEventSubscriber<DungeonRoomMechanismsChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<DungeonRoomMechanismsChanged> message,
        CancellationToken cancellationToken)
    {
        var view = message.Payload.View;
        var audience = online.Snapshot().Where(session => view.Recipients.Contains(session.CharacterId)).ToArray();
        var result = await events.ProjectAsync(new NotificationAudience.TheseSessions(audience),
            new DungeonRoomMechanismsNotification(view), cancellationToken).ConfigureAwait(false);
        if (result.Attempts.Any(attempt => attempt.Outcome == NotificationOutcome.Failed))
            throw new IOException("Room mechanism projection failed; the current snapshot will be retried.");
    }
}
