using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MagicSeal;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.MagicSeal;

/// <summary>What the MagicSeal slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class MagicSealRegistration
{
    internal static IServiceCollection AddMagicSealSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<MagicSealCompleted,
            MagicSealCompletedHandler>();
        services.AddGameCmdMessageDecoder<UnsealRandomOptionDecoder>(
            GameProtocol.UnsealRandomOptionType, ClientMessageType.GameUnsealRandomOption);
        services.AddGameCmdMessageDecoder<ChangeRandomOptionDecoder>(
            GameProtocol.ChangeRandomOptionType, ClientMessageType.GameChangeRandomOption);
        services.AddGameCmdMessageEncoder<ChangeRandomOptionResponse, ChangeRandomOptionEncoder>();
        services.AddProtocolCommandHandler<GameMagicSealCommand, EquipmentMagicSealResult, GameMagicSealCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameUnsealRandomOption, typeof(MagicSealRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameChangeRandomOption, typeof(MagicSealRequest)));
        services.RegisterProtocolMessageHandler<MagicSealHandler, MagicSealRequest, GameMagicSealCommand>(ClientMessageType.GameUnsealRandomOption);
        services.RegisterProtocolMessageHandler<MagicSealHandler, MagicSealRequest, GameMagicSealCommand>(ClientMessageType.GameChangeRandomOption);
        return services;
    }
}
