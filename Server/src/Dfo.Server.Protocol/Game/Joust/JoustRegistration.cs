using Dfo.Server.Protocol.Game.Joust;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Joust;

/// <summary>What the Joust slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class JoustRegistration
{
    internal static IServiceCollection AddJoustSlice(this IServiceCollection services)
    {

        services.RegisterProtocolMessageHandler<JoustQueryHandler, JoustQueryRequest, GameJoustQueryCommand>(ClientMessageType.GameJoustInfo);
        services.AddProtocolCommandHandler<GameJoustQueryCommand, bool, GameJoustQueryCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameJoustInfo, typeof(JoustQueryRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameJoustHistory, typeof(JoustQueryRequest)));
        services.RegisterProtocolMessageHandler<JoustQueryHandler, JoustQueryRequest, GameJoustQueryCommand>(ClientMessageType.GameJoustHistory);
        services.RegisterProtocolMessageHandler<JoustBetHandler, JoustBetRequest, GameJoustBetCommand>(ClientMessageType.GameJoustBet);
        services.AddProtocolCommandHandler<GameJoustBetCommand, bool, GameJoustBetCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameJoustBet, typeof(JoustBetRequest)));
        services.AddGameCmdMessageDecoder<JoustQueryDecoder>(0x04E6,ClientMessageType.GameJoustInfo);
        services.AddGameCmdMessageDecoder<JoustQueryDecoder>(0x04E8,ClientMessageType.GameJoustHistory);
        services.AddGameCmdMessageDecoder<JoustBetDecoder>(0x04E7,ClientMessageType.GameJoustBet);
        services.AddGameCmdMessageEncoder<JoustResponse,JoustResponseEncoder>();
        services.AddGameNotiMessageEncoder<JoustStateMessage,GameNotiStateSynchronizationEncoder<JoustStateMessage>>();
        services.AddGameNotiMessageEncoder<JoustInfoMessage,GameNotiStateSynchronizationEncoder<JoustInfoMessage>>();
        services.AddGameNotiMessageEncoder<JoustBetsMessage,GameNotiStateSynchronizationEncoder<JoustBetsMessage>>();
        services.AddGameNotiMessageEncoder<JoustResultsMessage,GameNotiStateSynchronizationEncoder<JoustResultsMessage>>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.Events.JoustDetailsChanged,JoustDetailsChangedHandler>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.Events.JoustLifecycleNotification,JoustLifecycleNotificationHandler>();
        return services;
    }
}
