using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.World;
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
            ClientMessageType.GameStartAntonRaid,
            ClientMessageType.GameRejoinAntonRaid,
            ClientMessageType.GameRenameAntonRaid,
            ClientMessageType.GameRequestAntonRaidTickets,
            ClientMessageType.GameSetAntonRaidSymbol,
            ClientMessageType.GameActivateAntonRaidBuff,
            ClientMessageType.GameReportAntonRaidMonster,
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
        services.AddGameNotiMessageEncoder<AntonRaidRuntimeNotice, AntonRaidRuntimeNoticeEncoder>();
        services.AddNotificationHandler<AntonRaidTownArrived, AntonRaidTownArrivedHandler>();
        services.AddNotificationHandler<AntonRaidSessionArrived, AntonRaidSessionArrivedHandler>();
        services.AddNotificationHandler<AntonRaidRecruitmentChanged, AntonRaidRecruitmentChangedHandler>();
        services.AddNotificationHandler<AntonRaidApprovalIssued, AntonRaidApprovalIssuedHandler>();
        services.AddNotificationHandler<AntonRaidRuntimeChanged, AntonRaidRuntimeChangedHandler>();
        services.AddDomainSubscriber<Dfo.Server.Abstractions.Raids.AntonRaidCombatChanged, AntonRaidCombatNotificationSubscriber>(
            "raid.anton-combat-wire", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<Dfo.Server.Abstractions.Raids.AntonRaidPhaseConcluded, AntonRaidPhaseRewardSubscriber>(
            "raid.anton-phase-rewards", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.Reliable);
        services.AddTransient<IAntonRaidRewardPhaseBridge, AntonRaidRewardPhaseBridge>();
        services.AddAntonRaidRewardsSlice();
        services.AddTransient<AntonRaidSquadProjection>();
        services.AddTransient<DungeonTownReturnProjection>();
        services.AddDungeonDeadlineSource<AntonRaidDeadlineSource>();
    }
}
