using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuardianAltar;

/// <summary>What the GuardianAltar slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class GuardianAltarRegistration
{
    internal static IServiceCollection AddGuardianAltarSlice(this IServiceCollection services)
    {
        services.AddDungeonDeadlineSource<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarDeadlineSource>();
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarHandler, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest, Dfo.Server.Protocol.Game.GuardianAltar.GameGuardianAltarCommand>(ClientMessageType.GameGuardianAltarOpen);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarHandler, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest, Dfo.Server.Protocol.Game.GuardianAltar.GameGuardianAltarCommand>(ClientMessageType.GameGuardianAltarBuy);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarHandler, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest, Dfo.Server.Protocol.Game.GuardianAltar.GameGuardianAltarCommand>(ClientMessageType.GameGuardianAltarSetSlot);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarHandler, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest, Dfo.Server.Protocol.Game.GuardianAltar.GameGuardianAltarCommand>(ClientMessageType.GameGuardianAltarUpgradeEnergy);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarHandler, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest, Dfo.Server.Protocol.Game.GuardianAltar.GameGuardianAltarCommand>(ClientMessageType.GameGuardianAltarSummon);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarHandler, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest, Dfo.Server.Protocol.Game.GuardianAltar.GameGuardianAltarCommand>(ClientMessageType.GameGuardianAltarExchange);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarHandler, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest, Dfo.Server.Protocol.Game.GuardianAltar.GameGuardianAltarCommand>(ClientMessageType.GameGuardianAltarPause);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarHandler, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest, Dfo.Server.Protocol.Game.GuardianAltar.GameGuardianAltarCommand>(ClientMessageType.GameGuardianAltarAchievement);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarHandler, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest, Dfo.Server.Protocol.Game.GuardianAltar.GameGuardianAltarCommand>(ClientMessageType.GameGuardianAltarReset);
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.GuardianAltar.GameGuardianAltarCommand, bool, Dfo.Server.Protocol.Game.GuardianAltar.GameGuardianAltarCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuardianAltarOpen, typeof(Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuardianAltarBuy, typeof(Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuardianAltarSetSlot, typeof(Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuardianAltarUpgradeEnergy, typeof(Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuardianAltarSummon, typeof(Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuardianAltarExchange, typeof(Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuardianAltarPause, typeof(Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuardianAltarAchievement, typeof(Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuardianAltarReset, typeof(Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarRequest)));
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarOpenDecoder>(0x0392, ClientMessageType.GameGuardianAltarOpen);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarBuyDecoder>(0x0393, ClientMessageType.GameGuardianAltarBuy);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarSetSlotDecoder>(0x0394, ClientMessageType.GameGuardianAltarSetSlot);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarUpgradeDecoder>(0x0395, ClientMessageType.GameGuardianAltarUpgradeEnergy);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarSummonDecoder>(0x0396, ClientMessageType.GameGuardianAltarSummon);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarExchangeDecoder>(0x0397, ClientMessageType.GameGuardianAltarExchange);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarPauseDecoder>(0x0398, ClientMessageType.GameGuardianAltarPause);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarAchievementDecoder>(0x0399, ClientMessageType.GameGuardianAltarAchievement);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarResetDecoder>(0x039A, ClientMessageType.GameGuardianAltarReset);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarOpenResponse, GameCmdStateSynchronizationEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarOpenResponse>>();
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarActionResponse, GameCmdStateSynchronizationEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarActionResponse>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarProfileMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarProfileMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarStageInfoMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarStageInfoMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarMapMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarMapMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarSpawnMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarSpawnMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarResultMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarResultMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarEplpMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarEplpMessage>>();
        return services;
    }
}
