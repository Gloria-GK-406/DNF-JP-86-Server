using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.PvpRoom;

/// <summary>What the PvpRoom slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class PvpRoomRegistration
{
    internal static IServiceCollection AddPvpRoomSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<MakePvpRoomDecoder>(ClientMessageType.GameMakePvpRoom);
        services.AddGameCmdMessageDecoder<EnterPvpRoomDecoder>(ClientMessageType.GameEnterPvpRoom);
        services.AddGameCmdMessageDecoder<QuickJoinPvpRoomDecoder>(ClientMessageType.GameQuickJoinPvpRoom);
        services.AddGameCmdMessageDecoder<SetPvpSeatStateDecoder>(ClientMessageType.GameSetPvpSeatState);
        services.AddGameCmdMessageDecoder<SetPvpReadyStateDecoder>(ClientMessageType.GameSetPvpReadyState);
        services.AddGameCmdMessageDecoder<SetPvpTeamModeDecoder>(ClientMessageType.GameSetPvpTeamMode);
        services.AddGameCmdMessageDecoder<SetPvpMapIndexDecoder>(ClientMessageType.GameSetPvpMapIndex);
        services.AddGameCmdMessageEncoder<PvpRoomCommandRefusal, PvpRoomCommandRefusalEncoder>();
        services.AddGameCmdMessageEncoder<EnterPvpRoomAccepted, EnterPvpRoomAcceptedEncoder>();
        services.AddGameCmdMessageEncoder<QuickJoinPvpRoomAccepted, QuickJoinPvpRoomAcceptedEncoder>();
        services.AddGameNotiMessageEncoder<PvpRoomInfoMessage, PvpRoomInfoEncoder>();
        services.AddGameNotiMessageEncoder<PvpRoomStateMessage, PvpRoomStateEncoder>();
        services.AddGameNotiMessageEncoder<PvpSeatStateMessage, PvpSeatStateEncoder>();
        services.AddGameNotiMessageEncoder<PvpReadyStateMessage, PvpReadyStateEncoder>();
        services.AddGameNotiMessageEncoder<StartPvpMessage, StartPvpEncoder>();
        services.AddNotificationHandler<PvpRoomBroadcast, PvpRoomBroadcastHandler>();
        services.AddProtocolCommandHandler<GameMakePvpRoomCommand, bool, GamePvpRoomCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameMakePvpRoom, typeof(MakePvpRoomRequest)));
        services.AddProtocolCommandHandler<GameEnterPvpRoomCommand, bool, GamePvpRoomCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameEnterPvpRoom, typeof(EnterPvpRoomRequest)));
        services.AddProtocolCommandHandler<GameQuickJoinPvpRoomCommand, bool, GamePvpRoomCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameQuickJoinPvpRoom, typeof(QuickJoinPvpRoomRequest)));
        services.AddProtocolCommandHandler<GameSetPvpSeatStateCommand, bool, GamePvpRoomCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetPvpSeatState, typeof(SetPvpSeatStateRequest)));
        services.AddProtocolCommandHandler<GameSetPvpReadyStateCommand, bool, GamePvpRoomCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetPvpReadyState, typeof(SetPvpReadyStateRequest)));
        services.AddProtocolCommandHandler<GameSetPvpTeamModeCommand, bool, GamePvpRoomCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetPvpTeamMode, typeof(SetPvpTeamModeRequest)));
        services.AddProtocolCommandHandler<GameSetPvpMapIndexCommand, bool, GamePvpRoomCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetPvpMapIndex, typeof(SetPvpMapIndexRequest)));
        services.RegisterProtocolMessageHandler<MakePvpRoomHandler, MakePvpRoomRequest, GameMakePvpRoomCommand>(ClientMessageType.GameMakePvpRoom);
        services.RegisterProtocolMessageHandler<EnterPvpRoomHandler, EnterPvpRoomRequest, GameEnterPvpRoomCommand>(ClientMessageType.GameEnterPvpRoom);
        services.RegisterProtocolMessageHandler<QuickJoinPvpRoomHandler, QuickJoinPvpRoomRequest, GameQuickJoinPvpRoomCommand>(ClientMessageType.GameQuickJoinPvpRoom);
        services.RegisterProtocolMessageHandler<SetPvpSeatStateHandler, SetPvpSeatStateRequest, GameSetPvpSeatStateCommand>(ClientMessageType.GameSetPvpSeatState);
        services.RegisterProtocolMessageHandler<SetPvpReadyStateHandler, SetPvpReadyStateRequest, GameSetPvpReadyStateCommand>(ClientMessageType.GameSetPvpReadyState);
        services.RegisterProtocolMessageHandler<SetPvpTeamModeHandler, SetPvpTeamModeRequest, GameSetPvpTeamModeCommand>(ClientMessageType.GameSetPvpTeamMode);
        services.RegisterProtocolMessageHandler<SetPvpMapIndexHandler, SetPvpMapIndexRequest, GameSetPvpMapIndexCommand>(ClientMessageType.GameSetPvpMapIndex);
        return services;
    }
}
