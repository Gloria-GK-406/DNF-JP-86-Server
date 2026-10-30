using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.BossTowerTicket;

/// <summary>What the BossTowerTicket slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class BossTowerTicketRegistration
{
    internal static IServiceCollection AddBossTowerTicketSlice(this IServiceCollection services)
    {
        services.AddDomainSubscriber<BossTowerTicketRefilled, Dfo.Server.Protocol.Game.BossTowerTicket.BossTowerTicketNotificationSubscriber>(
            "boss-tower.ticket-notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.BossTowerTicket.BossTowerTicketRefresh,
            Dfo.Server.Protocol.Game.BossTowerTicket.BossTowerTicketRefreshHandler>();
        return services;
    }
}
