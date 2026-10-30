using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ReinforceItem;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ReinforceItem;

/// <summary>What the ReinforceItem slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ReinforceItemRegistration
{
    internal static IServiceCollection AddReinforceItemSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<EquipmentReinforcementCompleted,
            EquipmentReinforcementCompletedHandler>();
        services.AddProtocolCommandHandler<GameReinforceItemCommand, EquipmentReinforcementResult, GameReinforceItemCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameReinforceItem, typeof(ReinforceItemRequest)));
        services.AddGameCmdMessageDecoder<ReinforceItemMessageDecoder>(
            GameProtocol.ReinforceItemType, ClientMessageType.GameReinforceItem);
        services.AddGameCmdMessageEncoder<ReinforceItemResponse, ReinforceItemResponseEncoder>();
        services.RegisterProtocolMessageHandler<ReinforceItemHandler, ReinforceItemRequest, GameReinforceItemCommand>(ClientMessageType.GameReinforceItem);
        return services;
    }
}
