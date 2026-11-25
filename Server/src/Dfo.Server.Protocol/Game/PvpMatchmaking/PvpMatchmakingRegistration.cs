using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.PvpMatchmaking;

internal static class PvpMatchmakingRegistration
{
    internal static IServiceCollection AddPvpMatchmakingSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<JoinPvpMatchmakingDecoder>(ClientMessageType.GameJoinPvpMatchmaking);
        services.AddGameCmdMessageDecoder<CancelPvpMatchmakingDecoder>(ClientMessageType.GameCancelPvpMatchmaking);
        services.AddGameCmdMessageDecoder<ConfirmPvpMatchmakingDecoder>(ClientMessageType.GameConfirmPvpMatchmaking);
        services.AddGameCmdMessageDecoder<PvpMatchmakingCountDecoder>(ClientMessageType.GamePvpMatchmakingCount);
        services.AddGameCmdMessageEncoder<JoinPvpMatchmakingResponse, JoinPvpMatchmakingResponseEncoder>();
        services.AddGameCmdMessageEncoder<CancelPvpMatchmakingResponse, CancelPvpMatchmakingResponseEncoder>();
        services.AddGameNotiMessageEncoder<PvpMatchmakingCountNotice, PvpMatchmakingCountEncoder>();
        services.AddGameNotiMessageEncoder<PvpMatchmakingRoster, PvpMatchmakingRosterEncoder>();
        services.AddGameNotiMessageEncoder<PvpMatchmakingStateNotice, PvpMatchmakingStateEncoder>();
        services.AddGameNotiMessageEncoder<PvpActorLoaded, PvpActorLoadedEncoder>();
        services.AddGameNotiMessageEncoder<PvpActorsConnected, PvpActorsConnectedEncoder>();
        services.AddGameNotiMessageEncoder<PvpMatchmakingComplete, PvpMatchmakingCompleteEncoder>();
        services.AddProtocolCommandHandler<GameJoinPvpMatchmakingCommand, bool, GamePvpMatchmakingCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameJoinPvpMatchmaking, typeof(JoinPvpMatchmakingRequest)));
        services.AddProtocolCommandHandler<GameCancelPvpMatchmakingCommand, bool, GamePvpMatchmakingCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCancelPvpMatchmaking, typeof(CancelPvpMatchmakingRequest)));
        services.AddProtocolCommandHandler<GameConfirmPvpMatchmakingCommand, bool, GamePvpMatchmakingCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameConfirmPvpMatchmaking, typeof(ConfirmPvpMatchmakingRequest)));
        services.AddProtocolCommandHandler<GamePvpMatchmakingCountCommand, bool, GamePvpMatchmakingCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GamePvpMatchmakingCount, typeof(PvpMatchmakingCountRequest)));
        services.RegisterProtocolMessageHandler<JoinPvpMatchmakingHandler, JoinPvpMatchmakingRequest, GameJoinPvpMatchmakingCommand>(
            ClientMessageType.GameJoinPvpMatchmaking);
        services.RegisterProtocolMessageHandler<CancelPvpMatchmakingHandler, CancelPvpMatchmakingRequest, GameCancelPvpMatchmakingCommand>(
            ClientMessageType.GameCancelPvpMatchmaking);
        services.RegisterProtocolMessageHandler<ConfirmPvpMatchmakingHandler, ConfirmPvpMatchmakingRequest, GameConfirmPvpMatchmakingCommand>(
            ClientMessageType.GameConfirmPvpMatchmaking);
        services.RegisterProtocolMessageHandler<PvpMatchmakingCountHandler, PvpMatchmakingCountRequest, GamePvpMatchmakingCountCommand>(
            ClientMessageType.GamePvpMatchmakingCount);
        return services;
    }
}
