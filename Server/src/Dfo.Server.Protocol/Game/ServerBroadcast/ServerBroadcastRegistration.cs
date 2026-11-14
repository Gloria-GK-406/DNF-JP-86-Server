using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.ServerBroadcast;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ServerBroadcast;

/// <summary>What the ServerBroadcast slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ServerBroadcastRegistration
{
    internal static IServiceCollection AddServerBroadcastSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<BoxRewardBroadcast, ServerBroadcastEncoder<BoxRewardBroadcast>>();
        services.AddGameNotiMessageEncoder<SecondAwakeningBroadcast, ServerBroadcastEncoder<SecondAwakeningBroadcast>>();
        services.AddNotificationHandler<ServerBroadcastPosted, ServerBroadcastPostedHandler>();
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
