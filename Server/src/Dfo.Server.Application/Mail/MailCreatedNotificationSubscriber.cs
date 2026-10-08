using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Notifications;

namespace Dfo.Server.Application.Mail;

internal sealed class MailCreatedNotificationSubscriber(IEventService events) : IDomainEventSubscriber<MailCreated>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<MailCreated> message, CancellationToken cancellationToken)
    {
        var recipient = message.Payload.Recipient;
        await events.ProjectAsync(new NotificationAudience.TheseCharacters([recipient]),
            new MailDelivered(recipient), cancellationToken);
    }
}
