using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.TauKingdom;

/// <summary>What the TauKingdom slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class TauKingdomRegistration
{
    internal static IServiceCollection AddTauKingdomSlice(this IServiceCollection services)
    {
        services.AddDomainSubscriber<TauKingdomTicketRefilled, Dfo.Server.Protocol.Game.TauKingdom.TauTicketNotificationSubscriber>(
            "tau-kingdom.ticket-notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.TauKingdom.TauTicketRefresh,
            Dfo.Server.Protocol.Game.TauKingdom.TauTicketRefreshHandler>();
        return services;
    }
}
