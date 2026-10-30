using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CharacterName;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.UnansweredReports;
using Dfo.Server.Protocol.Game.HiddenCharacterInfo;
using Dfo.Server.Protocol.Game.MapEntryReports;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol.Game.Party;

/// <summary>What the Party slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class PartyRegistration
{
    internal static IServiceCollection AddPartySlice(this IServiceCollection services)
    {
        services.TryAddSingleton<PartyClientIdRegistry>();
        // The client-facing party service wraps the Application one, which it takes by its concrete type. Replace
        // leaves only this registration whichever module runs first: Application only TryAdds IPartyService.
        services.Replace(ServiceDescriptor.Singleton<IPartyService, PartyClientProjectionService>());
        services.TryAddSingleton<PartyProjectionStreamRegistry>();
        services.TryAddSingleton<IPartyProjectionStateDomainService>(provider => provider.GetRequiredService<PartyProjectionStreamRegistry>());
        services.TryAddSingleton<PartyNotificationDelivery>();
        services.TryAddSingleton<IPartyTownProjectionDelivery, PartyTownProjectionDelivery>();
        services.AddNotificationHandler<PartyInviteIssued, PartyInviteIssuedHandler>();
        services.AddNotificationHandler<PartyRealtimeChanged, PartyRealtimeChangedHandler>();
        services.AddNotificationHandler<PartyInviteAccepted, PartyInviteAcceptedHandler>();
        services.AddNotificationHandler<PartyEndpointsChanged, PartyEndpointsChangedHandler>();
        services.AddNotificationHandler<PartyRosterChanged, PartyRosterChangedHandler>();
        services.AddNotificationHandler<PartySettingsChanged, PartySettingsChangedHandler>();
        services.AddNotificationHandler<PartyLeadershipChanged, PartyLeadershipChangedHandler>();
        services.AddGameCmdMessageDecoder<SetUdpIpPortDecoder>(
            GameProtocol.SetUdpIpPortType, ClientMessageType.GameSetUdpIpPort);
        services.AddGameCmdMessageDecoder<RequestPeerDecoder>(
            GameProtocol.RequestPeerType, ClientMessageType.GameRequestPeer);
        services.AddGameCmdMessageDecoder<ResponsePeerDecoder>(
            GameProtocol.ResponsePeerType, ClientMessageType.GameResponsePeer);
        services.AddGameCmdMessageDecoder<LeavePartyDecoder>(
            GameProtocol.LeavePartyType, ClientMessageType.GameLeaveParty);
        services.AddGameCmdMessageDecoder<RemovePartyMemberDecoder>(
            GameProtocol.RemovePartyMemberType, ClientMessageType.GameRemovePartyMember);
        services.AddGameCmdMessageDecoder<ChangePartyLeaderDecoder>(
            GameProtocol.ChangePartyLeaderType, ClientMessageType.GameChangePartyLeader);
        services.AddGameCmdMessageDecoder<CallPartyMemberRealtimeInfoDecoder>(
            GameProtocol.CallPartyMemberRealtimeInfoType,
            ClientMessageType.GameCallPartyMemberRealtimeInfo);
        services.AddGameCmdMessageDecoder<SetPartyInfoDecoder>(
            GameProtocol.SetPartyInfoType,
            ClientMessageType.GameSetPartyInfo);
        services.AddGameNotiMessageEncoder<PartyInviteRequestMessage, PartyInviteRequestEncoder>();
        services.AddGameNotiMessageEncoder<PartyInviteAcceptedMessage, PartyInviteAcceptedEncoder>();
        services.AddGameNotiMessageEncoder<PartyRealtimeMessage, PartyRealtimeEncoder>();
        services.AddGameNotiMessageEncoder<PartyEndpointsMessage, PartyEndpointsEncoder>();
        services.AddGameNotiMessageEncoder<PartyRosterMessage, PartyRosterEncoder>();
        services.AddGameNotiMessageEncoder<PartySettingsMessage, PartySettingsEncoder>();
        services.AddGameCmdMessageEncoder<
            RemovePartyMemberRefusal,
            RemovePartyMemberRefusalEncoder>();
        // Turning a card over now hands the piece behind it to the character's bag, which is the
        // moment the note left here foresaw: a claim on a run alone is memory, and a row in a bag
        // is not. Transactional from that change onwards, on the same reading the ground pick-up
        // was given -- the claim and the row are one change, and a position marked taken whose
        // piece never arrived anywhere is the thing to avoid. The bag's own container refuses a
        // write with no boundary open, so this is what makes the arrival possible at all.
        services.AddProtocolCommandHandler<GameLeavePartyCommand, bool, GamePartyTransitionCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameLeaveParty, typeof(LeavePartyRequest)));
        services.AddProtocolCommandHandler<GameRemovePartyMemberCommand, bool, GamePartyTransitionCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRemovePartyMember, typeof(RemovePartyMemberRequest)));
        services.AddProtocolCommandHandler<GameChangePartyLeaderCommand, bool, GamePartyTransitionCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameChangePartyLeader, typeof(ChangePartyLeaderRequest)));
        services.AddProtocolCommandHandler<GameSetUdpIpPortCommand, bool, GameSetUdpIpPortCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetUdpIpPort, typeof(SetUdpIpPortRequest)));
        services.AddProtocolCommandHandler<GameRequestPeerCommand, bool, GameRequestPeerCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRequestPeer, typeof(RequestPeerRequest)));
        services.AddProtocolCommandHandler<GameResponsePeerCommand, bool, GameResponsePeerCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameResponsePeer, typeof(ResponsePeerRequest)));
        services.AddProtocolCommandHandler<GameSetPartyInfoCommand, bool, GameSetPartyInfoCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetPartyInfo, typeof(SetPartyInfoRequest)));
        services.AddProtocolCommandHandler<GameCallPartyMemberRealtimeInfoCommand, bool, GameCallPartyMemberRealtimeInfoCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCallPartyMemberRealtimeInfo, typeof(CallPartyMemberRealtimeInfoRequest)));

        services.RegisterProtocolMessageHandler<SetUdpIpPortHandler, SetUdpIpPortRequest, GameSetUdpIpPortCommand>(ClientMessageType.GameSetUdpIpPort);
        services.RegisterProtocolMessageHandler<RequestPeerHandler, RequestPeerRequest, GameRequestPeerCommand>(ClientMessageType.GameRequestPeer);
        services.RegisterProtocolMessageHandler<ResponsePeerHandler, ResponsePeerRequest, GameResponsePeerCommand>(ClientMessageType.GameResponsePeer);
        services.RegisterProtocolMessageHandler<LeavePartyHandler, LeavePartyRequest, GameLeavePartyCommand>(ClientMessageType.GameLeaveParty);
        services.RegisterProtocolMessageHandler<RemovePartyMemberHandler, RemovePartyMemberRequest, GameRemovePartyMemberCommand>(
            ClientMessageType.GameRemovePartyMember);
        services.RegisterProtocolMessageHandler<ChangePartyLeaderHandler, ChangePartyLeaderRequest, GameChangePartyLeaderCommand>(
            ClientMessageType.GameChangePartyLeader);
        services.RegisterProtocolMessageHandler<CallPartyMemberRealtimeInfoHandler, CallPartyMemberRealtimeInfoRequest, GameCallPartyMemberRealtimeInfoCommand>(
            ClientMessageType.GameCallPartyMemberRealtimeInfo);
        services.RegisterProtocolMessageHandler<SetPartyInfoHandler, SetPartyInfoRequest, GameSetPartyInfoCommand>(ClientMessageType.GameSetPartyInfo);
        return services;
    }
}
