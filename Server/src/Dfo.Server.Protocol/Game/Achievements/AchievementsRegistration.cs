using Dfo.Server.Protocol.Game.Achievements;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Achievements;

/// <summary>What the Achievements slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class AchievementsRegistration
{
    internal static IServiceCollection AddAchievementsSlice(this IServiceCollection services)
    {
        services.AddProtocolCommandHandler<GameAchievementTriggerCommand, bool, GameAchievementTriggerCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameAchievementTrigger, typeof(AchievementTriggerRequest)));
        services.AddDomainSubscriber<AchievementProgressChanged, AchievementEventSubscriber>("achievement.noti", DomainEventDeliveryPolicy.LiveOnly, f => f.CharacterId.Value.ToString());
        services.AddNotificationHandler<AchievementChanged, AchievementChangedHandler>();
        services.RegisterProtocolMessageHandler<AchievementTriggerHandler, AchievementTriggerRequest, GameAchievementTriggerCommand>(ClientMessageType.GameAchievementTrigger);
        services.AddGameCmdMessageDecoder<AchievementTriggerDecoder>(ClientMessageType.GameAchievementTrigger);
        services.AddGameCmdMessageEncoder<AchievementTriggerResponse, AchievementTriggerEncoder>();
        return services;
    }
}
