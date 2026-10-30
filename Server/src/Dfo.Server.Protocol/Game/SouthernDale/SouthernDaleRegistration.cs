using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.SouthernDale;

/// <summary>What the SouthernDale slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class SouthernDaleRegistration
{
    internal static IServiceCollection AddSouthernDaleSlice(this IServiceCollection services)
    {
        services.AddDomainSubscriber<SouthernDaleInventoryChanged, Dfo.Server.Protocol.Game.SouthernDale.SouthernInventoryNotificationSubscriber>(
            "southern-dale.inventory-notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<SouthernDaleDailyRefillCompleted, Dfo.Server.Protocol.Game.SouthernDale.SouthernDailyRefillNotificationSubscriber>(
            "southern-dale.daily-refill-notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.SouthernDale.SouthernInventoryRefresh,
            Dfo.Server.Protocol.Game.SouthernDale.SouthernInventoryRefreshHandler>();
        return services;
    }
}
