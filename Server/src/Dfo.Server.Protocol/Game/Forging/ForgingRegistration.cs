using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Forging;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Forging;

/// <summary>What the Forging slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ForgingRegistration
{
    internal static IServiceCollection AddForgingSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<ForgingCompleted,
            ForgingCompletedHandler>();
        services.AddGameCmdMessageDecoder<ForgingDecoder>(
            GameProtocol.SeparateUpgradeType, ClientMessageType.GameSeparateUpgrade);
        services.AddGameCmdMessageEncoder<ForgingResponse, ForgingEncoder>();
        services.AddProtocolCommandHandler<GameForgingCommand, EquipmentForgingResult, GameForgingCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameSeparateUpgrade, typeof(ForgingRequest)));
        services.RegisterProtocolMessageHandler<ForgingHandler, ForgingRequest, GameForgingCommand>(ClientMessageType.GameSeparateUpgrade);
        return services;
    }
}
