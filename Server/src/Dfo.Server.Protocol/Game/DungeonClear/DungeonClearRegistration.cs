using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.DungeonClear;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.DungeonClear;

/// <summary>What the DungeonClear slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class DungeonClearRegistration
{
    internal static IServiceCollection AddDungeonClearSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<EnableClearDungeonNotification, EnableClearDungeonEncoder>();
        services.AddGameNotiMessageEncoder<SecretShopNpcNotification, SecretShopNpcEncoder>();
        services.AddGameNotiMessageEncoder<PlayResultNotification, PlayResultEncoder>();
        services.AddGameNotiMessageEncoder<ClearDungeonRewardNotification, ClearDungeonRewardEncoder>();
        services.AddGameCmdMessageDecoder<SetPlayResultDecoder>(
            GameProtocol.SetPlayResultType, ClientMessageType.GameSetPlayResult);
        services.AddGameCmdMessageDecoder<BossDieCheckDecoder>(
            GameProtocol.BossDieCheckType, ClientMessageType.GameBossDieCheck);
        services.AddGameCmdMessageDecoder<ScoreScrollStateDecoder>(
            GameProtocol.ScoreScrollStateType, ClientMessageType.GameScoreScrollState);
        services.AddGameCmdMessageDecoder<EplpCommandDecoder>(
            GameProtocol.EplpCommandType, ClientMessageType.GameEplpCommand);
        services.AddGameCmdMessageDecoder<GiveUpGameDecoder>(
            GameProtocol.GiveUpGameType, ClientMessageType.GameGiveUpGame);
        services.AddGameCmdMessageEncoder<
            ScoreScrollStateResponse, ScoreScrollStateResponseEncoder>();
        services.AddGameCmdMessageEncoder<EplpCommandResponse, EplpCommandResponseEncoder>();
        services.AddGameCmdMessageEncoder<GiveUpGameResponse, GiveUpGameResponseEncoder>();
        services.AddProtocolCommandHandler<GameSetPlayResultCommand, bool, GameSetPlayResultCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetPlayResult, typeof(SetPlayResultRequest)));
        services.RegisterProtocolMessageHandler<SetPlayResultHandler, SetPlayResultRequest, GameSetPlayResultCommand>(ClientMessageType.GameSetPlayResult);
        services.AddProtocolCommandHandler<GameBossDieCheckCommand, bool, GameBossDieCheckCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameBossDieCheck, typeof(BossDieCheckRequest)));
        services.RegisterProtocolMessageHandler<BossDieCheckHandler, BossDieCheckRequest, GameBossDieCheckCommand>(ClientMessageType.GameBossDieCheck);
        services.RegisterProtocolMessageHandler<ScoreScrollStateHandler, ScoreScrollStateRequest, GameScoreScrollStateCommand>(ClientMessageType.GameScoreScrollState);
        services.AddProtocolCommandHandler<GameEplpCommand, bool, GamePartyTransitionCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameEplpCommand, typeof(EplpCommandRequest)));
        services.RegisterProtocolMessageHandler<EplpCommandHandler, EplpCommandRequest, GameEplpCommand>(ClientMessageType.GameEplpCommand);
        services.AddProtocolCommandHandler<GameGiveUpGameCommand, bool, GamePartyTransitionCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameGiveUpGame, typeof(GiveUpGameRequest)));
        services.RegisterProtocolMessageHandler<GiveUpGameHandler, GiveUpGameRequest, GameGiveUpGameCommand>(ClientMessageType.GameGiveUpGame);
        return services;
    }
}
