using Dfo.Server.Application.World;
using Dfo.Server.Application.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Altar;

/// <summary>What the Altar slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class AltarRegistration
{
    internal static IServiceCollection AddAltarSlice(this IServiceCollection services)
    {
        services.AddDungeonDeadlineSource<Dfo.Server.Protocol.Game.Altar.AltarDeadlineSource>();
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.Altar.AltarStateChanged,
            Dfo.Server.Protocol.Game.Altar.AltarStateChangedHandler>();
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.Altar.AltarPrepareHandler, Dfo.Server.Protocol.Game.Altar.AltarPrepareRequest, Dfo.Server.Protocol.Game.Altar.GameAltarPrepareCommand>(ClientMessageType.GameAltarPrepare);
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.Altar.GameAltarPrepareCommand, bool, Dfo.Server.Protocol.Game.Altar.GameAltarCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameAltarPrepare, typeof(Dfo.Server.Protocol.Game.Altar.AltarPrepareRequest)));
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.Altar.AltarDeathBatchHandler, Dfo.Server.Protocol.Game.Altar.AltarDeathBatchRequest, Dfo.Server.Protocol.Game.Altar.GameAltarDeathBatchCommand>(ClientMessageType.GameAltarDeathBatch);
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.Altar.GameAltarDeathBatchCommand, bool, Dfo.Server.Protocol.Game.Altar.GameAltarCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameAltarDeathBatch, typeof(Dfo.Server.Protocol.Game.Altar.AltarDeathBatchRequest)));
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.Altar.AltarDifficultyHandler, Dfo.Server.Protocol.Game.Altar.AltarDifficultyRequest, Dfo.Server.Protocol.Game.Altar.GameAltarDifficultyCommand>(ClientMessageType.GameAltarDifficulty);
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.Altar.GameAltarDifficultyCommand, bool, Dfo.Server.Protocol.Game.Altar.GameAltarCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameAltarDifficulty, typeof(Dfo.Server.Protocol.Game.Altar.AltarDifficultyRequest)));
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.Altar.AltarPrepareDecoder>(0x0102, ClientMessageType.GameAltarPrepare);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.Altar.AltarDeathBatchDecoder>(0x00FF, ClientMessageType.GameAltarDeathBatch);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyDecoder>(0x013D, ClientMessageType.GameAltarDifficulty);
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarInfoMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarInfoMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarMapMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarMapMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarWaveMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarWaveMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarIntervalMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarIntervalMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyPromptMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyPromptMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyResolvedMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyResolvedMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarRankingMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarRankingMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarRewardMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarRewardMessage>>();
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyResponse, GameCmdStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyResponse>>();
        return services;
    }
}
