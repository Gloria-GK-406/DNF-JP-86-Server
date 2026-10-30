using Dfo.Server.Protocol.Game.DarkElfRuins;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.DarkElfRuins;

/// <summary>What the DarkElfRuins slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class DarkElfRuinsRegistration
{
    internal static IServiceCollection AddDarkElfRuinsSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<RuinsPlayResultDecoder>(ClientMessageType.GameRuinsPlayResult);
        services.AddGameCmdMessageDecoder<RuinsRewardDecoder>(ClientMessageType.GameRuinsReward);
        services.AddProtocolCommandHandler<GameRuinsPlayResultCommand, bool, GameRuinsPlayResultCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRuinsPlayResult, typeof(RuinsPlayResultRequest)));
        services.RegisterProtocolMessageHandler<RuinsPlayResultHandler, RuinsPlayResultRequest, GameRuinsPlayResultCommand>(ClientMessageType.GameRuinsPlayResult);
        services.AddProtocolCommandHandler<GameRuinsRewardCommand, bool, GameRuinsRewardCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRuinsReward, typeof(RuinsRewardRequest)));
        services.RegisterProtocolMessageHandler<RuinsRewardHandler, RuinsRewardRequest, GameRuinsRewardCommand>(ClientMessageType.GameRuinsReward);
        services.AddGameNotiMessageEncoder<RuinsByteNotification, GameNotiStateSynchronizationEncoder<RuinsByteNotification>>();
        services.AddGameNotiMessageEncoder<RuinsLicenseNotification, GameNotiStateSynchronizationEncoder<RuinsLicenseNotification>>();
        services.AddGameNotiMessageEncoder<RuinsClearNotification, GameNotiStateSynchronizationEncoder<RuinsClearNotification>>();
        services.AddGameNotiMessageEncoder<RuinsBossNotification, GameNotiStateSynchronizationEncoder<RuinsBossNotification>>();
        services.AddGameCmdMessageEncoder<RuinsAcknowledgement, GameCmdStateSynchronizationEncoder<RuinsAcknowledgement>>();
        services.AddDomainSubscriber<DarkElfRuinsInventoryChanged, RuinsInventorySubscriber>("darkelf-ruins.inventory-notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddNotificationHandler<RuinsInventoryRefresh, RuinsInventoryRefreshHandler>();
        return services;
    }
}
