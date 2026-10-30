using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Application.Mail;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Mail;

/// <summary>What the Mail domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class MailRegistration
{
    internal static IServiceCollection AddMail(this IServiceCollection services)
    {
        services.AddDomainSubscriber<MailCreated, MailCreatedNotificationSubscriber>("mail.arrival",
            Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.LiveOnly,
            fact => fact.Recipient.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        services.TryAddSingleton<MailItemTransfers>();
        services.TryAddSingleton<IMailboxQueryDomainService, MailboxQueryDomainService>();
        services.TryAddSingleton<IMailboxDomainService, MailboxDomainService>();
        services.TryAddSingleton<IMailboxAssetDomainService, MailboxAssetDomainService>();
        services.TryAddSingleton<IMailboxSendDomainService, MailboxSendDomainService>();
        return services;
    }
}
