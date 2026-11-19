using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal static class AntonRaidRegistration
{
    internal static void AddAntonRaidSlice(this IServiceCollection services)
    {
        ClientMessageType[] types =
        [
            ClientMessageType.GameCreateAntonRaid,
            ClientMessageType.GameLeaveAntonRaid,
            ClientMessageType.GameManageAntonRaid,
            ClientMessageType.GameCheckAntonRaidUser,
            ClientMessageType.GameRequestAntonRaidMembers,
            ClientMessageType.GameRequestAntonRaidDirectory,
        ];
        services.AddProtocolCommandHandler<GameAntonRaidCommand, bool, GameAntonRaidCommandHandler>(
            [.. types.Append(ClientMessageType.GameRequestAntonRaidPeer).Append(ClientMessageType.GameRespondAntonRaidPeer)
                .Select(type => new ProtocolCommandMessageBinding(type, typeof(AntonRaidRequest)))]);
        foreach (var type in types)
        {
            services.AddGameCmdMessageDecoder<AntonRaidRequestDecoder>(type);
            services.RegisterProtocolMessageHandler<AntonRaidMessageHandler, AntonRaidRequest, GameAntonRaidCommand>(type);
        }

        services.RegisterProtocolMessageHandler<AntonRaidMessageHandler, AntonRaidRequest, GameAntonRaidCommand>(ClientMessageType.GameRequestAntonRaidPeer);
        services.RegisterProtocolMessageHandler<AntonRaidMessageHandler, AntonRaidRequest, GameAntonRaidCommand>(ClientMessageType.GameRespondAntonRaidPeer);

        services.AddGameCmdMessageEncoder<AntonRaidReply, AntonRaidReplyEncoder>();
        services.AddGameNotiMessageEncoder<AntonRaidTownLoaded, AntonRaidTownLoadedEncoder>();
        services.AddGameNotiMessageEncoder<AntonRaidDirectoryNotice, AntonRaidDirectoryEncoder>();
        services.AddGameNotiMessageEncoder<AntonRaidChangedNotice, AntonRaidChangedEncoder>();
        services.AddGameNotiMessageEncoder<AntonRaidApprovalNotice, AntonRaidApprovalEncoder>();
        services.AddNotificationHandler<AntonRaidTownArrived, AntonRaidTownArrivedHandler>();
        services.AddNotificationHandler<AntonRaidRecruitmentChanged, AntonRaidRecruitmentChangedHandler>();
        services.AddNotificationHandler<AntonRaidApprovalIssued, AntonRaidApprovalIssuedHandler>();
    }
}
