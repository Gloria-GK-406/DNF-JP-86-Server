using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Outworld;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Outworld;

/// <summary>What the Outworld slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class OutworldRegistration
{
    internal static IServiceCollection AddOutworldSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<OutworldCompleted,
            OutworldCompletedHandler>();
        services.AddGameCmdMessageDecoder<PurifyItemDecoder>(ClientMessageType.GamePurifyItem);
        services.AddGameCmdMessageDecoder<InvestAmplifyOptionDecoder>(ClientMessageType.GameInvestAmplifyOption);
        services.AddGameCmdMessageEncoder<PurifyItemResponse, PurifyItemEncoder>();
        services.AddGameCmdMessageEncoder<InvestAmplifyOptionResponse, InvestAmplifyOptionEncoder>();
        services.AddProtocolCommandHandler<GameOutworldCommand, EquipmentOutworldResult, GameOutworldCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GamePurifyItem, typeof(OutworldRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameInvestAmplifyOption, typeof(OutworldRequest)));
        services.RegisterProtocolMessageHandler<OutworldHandler, OutworldRequest, GameOutworldCommand>(ClientMessageType.GamePurifyItem);
        services.RegisterProtocolMessageHandler<OutworldHandler, OutworldRequest, GameOutworldCommand>(ClientMessageType.GameInvestAmplifyOption);
        return services;
    }
}
