using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.ServerBroadcast;
using Dfo.Server.Application.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.ServerBroadcast;

internal static class ServerBroadcastRegistration
{
    internal static IServiceCollection AddServerBroadcast(this IServiceCollection services)
    {
        services.TryAddSingleton<IServerBroadcastRuleService, ServerBroadcastRuleService>();
        services.AddDomainEvent<EquipmentUpgradeAnnounced>("broadcast.equipment-upgrade", 1);
        services.AddDomainEvent<VendingPrizeAnnounced>("broadcast.vending-prize", 1);
        services.AddDomainEvent<BoxRewardAnnounced>("broadcast.box-reward", 1);
        services.AddDomainEvent<EquipmentForgingAnnounced>("broadcast.equipment-forging", 1);
        services.AddDomainEvent<RecipeCraftAnnounced>("broadcast.recipe-craft", 1);
        services.AddDomainEvent<DespairTowerFloorAnnounced>("broadcast.despair-tower-floor", 1);
        services.AddDomainEvent<AncientDungeonItemAnnounced>("broadcast.ancient-dungeon-item", 1);
        services.AddDomainSubscriber<CharacterLevelChanged, ServerBroadcastSubscriber>("broadcast.max-level", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<CharacterGrowTypeChanged, ServerBroadcastSubscriber>("broadcast.second-awakening", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<EquipmentUpgradeAnnounced, ServerBroadcastSubscriber>("broadcast.equipment-upgrade", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<VendingPrizeAnnounced, ServerBroadcastSubscriber>("broadcast.vending-prize", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<BoxRewardAnnounced, ServerBroadcastSubscriber>("broadcast.box-reward", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<EquipmentForgingAnnounced, ServerBroadcastSubscriber>("broadcast.equipment-forging", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<RecipeCraftAnnounced, ServerBroadcastSubscriber>("broadcast.recipe-craft", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<DespairTowerFloorAnnounced, ServerBroadcastSubscriber>("broadcast.despair-tower-floor", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<AncientDungeonItemAnnounced, ServerBroadcastSubscriber>("broadcast.ancient-dungeon-item", DomainEventDeliveryPolicy.LiveOnly);
        return services;
    }
}
