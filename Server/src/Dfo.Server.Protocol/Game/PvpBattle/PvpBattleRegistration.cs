using System.Collections.Immutable;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol.Game.PvpBattle;

/// <summary>What the PvpBattle slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
/// <remarks>
/// The deadline and shutdown commands stay with the other world maintenance commands
/// (<c>WorldMaintenanceCommandRegistration</c>); the departure commands they run first are registered here, with the
/// departure queue they read.
/// </remarks>
internal static class PvpBattleRegistration
{
    internal static IServiceCollection AddPvpBattleSlice(this IServiceCollection services)
    {
        services.TryAddSingleton<PvpDepartureProjectionQueue>();
        services.TryAddSingleton<IPvpDepartureProjectionDomainService>(p => p.GetRequiredService<PvpDepartureProjectionQueue>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPvpRoomDepartureSink, PvpDepartureProjectionQueue>(
            p => p.GetRequiredService<PvpDepartureProjectionQueue>()));
        services.TryAddSingleton<PvpResultScreenDeferrals>();
        services.TryAddSingleton<IPvpResultScreenDeferralDomainService>(p => p.GetRequiredService<PvpResultScreenDeferrals>());
        services.AddInternalCommandHandler<CapturePvpDeparturesCommand, ImmutableArray<PvpDepartureEntryId>, PvpBattleDeadlineCommandHandler>();
        services.AddInternalCommandHandler<ProjectPvpDepartureCommand, bool, PvpBattleDeadlineCommandHandler>();

        services.AddGameCmdMessageDecoder<CompleteLoadPvpDecoder>(ClientMessageType.GameCompleteLoadPvp);
        services.AddGameCmdMessageDecoder<ConnectP2pPvpDecoder>(ClientMessageType.GameConnectP2pPvp);
        services.AddGameCmdMessageDecoder<PvpHeartbeatDecoder>(ClientMessageType.GamePvpHeartbeat);
        services.AddGameCmdMessageDecoder<PvpRequestFightDecoder>(ClientMessageType.GamePvpRequestFight);
        services.AddGameCmdMessageDecoder<DiePvpCharacterDecoder>(ClientMessageType.GameDiePvpCharacter);
        services.AddGameCmdMessageDecoder<PvpTimeOutDecoder>(ClientMessageType.GamePvpTimeOut);
        services.AddGameCmdMessageDecoder<PvpRankResponseDecoder>(ClientMessageType.GamePvpRankResponse);
        services.AddGameCmdMessageDecoder<EndPvpResultDecoder>(ClientMessageType.GameEndPvpResult);
        services.AddGameNotiMessageEncoder<PvpDeathMessage, PvpDeathEncoder>();
        services.AddGameNotiMessageEncoder<PvpTurnPlayerMessage, PvpTurnPlayerEncoder>();
        services.AddGameNotiMessageEncoder<PvpRequestFightMessage, PvpRequestFightEncoder>();
        services.AddGameNotiMessageEncoder<PvpRankRequestMessage, PvpRankRequestEncoder>();
        services.AddGameNotiMessageEncoder<EndPvpMessage, EndPvpEncoder>();
        services.AddGameNotiMessageEncoder<PvpRecordMessage, PvpRecordEncoder>();
        services.AddProtocolCommandHandler<GameCompleteLoadPvpCommand, bool, GamePvpBattleCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCompleteLoadPvp, typeof(CompleteLoadPvpRequest)));
        services.AddProtocolCommandHandler<GameConnectP2pPvpCommand, bool, GamePvpBattleCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameConnectP2pPvp, typeof(ConnectP2pPvpRequest)));
        services.AddProtocolCommandHandler<GamePvpHeartbeatCommand, bool, GamePvpBattleCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GamePvpHeartbeat, typeof(PvpHeartbeatRequest)));
        services.AddProtocolCommandHandler<GamePvpRequestFightCommand, bool, GamePvpBattleCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GamePvpRequestFight, typeof(PvpRequestFightRequest)));
        services.AddProtocolCommandHandler<GameDiePvpCharacterCommand, bool, GamePvpBattleCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameDiePvpCharacter, typeof(DiePvpCharacterRequest)));
        services.AddProtocolCommandHandler<GamePvpTimeOutCommand, bool, GamePvpBattleCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GamePvpTimeOut, typeof(PvpTimeOutRequest)));
        services.AddProtocolCommandHandler<GamePvpRankResponseCommand, bool, GamePvpBattleCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GamePvpRankResponse, typeof(PvpRankResponseRequest)));
        services.AddProtocolCommandHandler<GameEndPvpResultCommand, bool, GamePvpBattleCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameEndPvpResult, typeof(EndPvpResultRequest)));
        services.RegisterProtocolMessageHandler<CompleteLoadPvpHandler, CompleteLoadPvpRequest, GameCompleteLoadPvpCommand>(ClientMessageType.GameCompleteLoadPvp);
        services.RegisterProtocolMessageHandler<ConnectP2pPvpHandler, ConnectP2pPvpRequest, GameConnectP2pPvpCommand>(ClientMessageType.GameConnectP2pPvp);
        services.RegisterProtocolMessageHandler<PvpHeartbeatHandler, PvpHeartbeatRequest, GamePvpHeartbeatCommand>(ClientMessageType.GamePvpHeartbeat);
        services.RegisterProtocolMessageHandler<PvpRequestFightHandler, PvpRequestFightRequest, GamePvpRequestFightCommand>(ClientMessageType.GamePvpRequestFight);
        services.RegisterProtocolMessageHandler<DiePvpCharacterHandler, DiePvpCharacterRequest, GameDiePvpCharacterCommand>(ClientMessageType.GameDiePvpCharacter);
        services.RegisterProtocolMessageHandler<PvpTimeOutHandler, PvpTimeOutRequest, GamePvpTimeOutCommand>(ClientMessageType.GamePvpTimeOut);
        services.RegisterProtocolMessageHandler<PvpRankResponseHandler, PvpRankResponseRequest, GamePvpRankResponseCommand>(ClientMessageType.GamePvpRankResponse);
        services.RegisterProtocolMessageHandler<EndPvpResultHandler, EndPvpResultRequest, GameEndPvpResultCommand>(ClientMessageType.GameEndPvpResult);
        return services;
    }
}
