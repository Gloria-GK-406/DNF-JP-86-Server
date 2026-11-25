using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.PvpBattle;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.PvpTotalMatchTeam;

internal static class PvpTotalMatchTeamRegistration
{
    internal static IServiceCollection AddPvpTotalMatchTeamSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<CheckPvpTotalMatchTeamNameDecoder>(ClientMessageType.GameCheckPvpTotalMatchTeamName);
        services.AddGameCmdMessageDecoder<SetPvpTotalMatchTeamDecoder>(ClientMessageType.GameSetPvpTotalMatchTeam);
        services.AddGameCmdMessageDecoder<SelectPvpTotalMatchCandidateDecoder>(ClientMessageType.GameSelectPvpTotalMatchCandidate);
        services.AddGameCmdMessageEncoder<PvpTotalMatchTeamReply, PvpTotalMatchTeamReplyEncoder>();
        services.AddGameNotiMessageEncoder<PvpTotalMatchTeamInfo, PvpTotalMatchTeamInfoEncoder>();
        services.AddGameNotiMessageEncoder<PvpTotalMatchCharacterInfo, PvpTotalMatchCharacterInfoEncoder>();
        services.AddGameNotiMessageEncoder<PvpTotalMatchInfo, PvpTotalMatchInfoEncoder>();
        services.AddGameNotiMessageEncoder<PvpTotalMatchSelectionStarted, PvpTotalMatchSelectionStartedEncoder>();
        services.AddGameNotiMessageEncoder<PvpTotalMatchFight, PvpTotalMatchFightEncoder>();
        services.AddProtocolCommandHandler<GameSelectPvpTotalMatchCandidateCommand, bool, GamePvpBattleCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSelectPvpTotalMatchCandidate, typeof(SelectPvpTotalMatchCandidateRequest)));
        services.RegisterProtocolMessageHandler<SelectPvpTotalMatchCandidateHandler, SelectPvpTotalMatchCandidateRequest, GameSelectPvpTotalMatchCandidateCommand>(
            ClientMessageType.GameSelectPvpTotalMatchCandidate);
        services.AddProtocolCommandHandler<GameCheckPvpTotalMatchTeamNameCommand, bool, PvpTotalMatchTeamCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCheckPvpTotalMatchTeamName, typeof(CheckPvpTotalMatchTeamNameRequest)));
        services.AddProtocolCommandHandler<GameSetPvpTotalMatchTeamCommand, bool, PvpTotalMatchTeamCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetPvpTotalMatchTeam, typeof(SetPvpTotalMatchTeamRequest)));
        services.RegisterProtocolMessageHandler<CheckPvpTotalMatchTeamNameHandler, CheckPvpTotalMatchTeamNameRequest, GameCheckPvpTotalMatchTeamNameCommand>(
            ClientMessageType.GameCheckPvpTotalMatchTeamName);
        services.RegisterProtocolMessageHandler<SetPvpTotalMatchTeamHandler, SetPvpTotalMatchTeamRequest, GameSetPvpTotalMatchTeamCommand>(
            ClientMessageType.GameSetPvpTotalMatchTeam);
        return services;
    }
}
