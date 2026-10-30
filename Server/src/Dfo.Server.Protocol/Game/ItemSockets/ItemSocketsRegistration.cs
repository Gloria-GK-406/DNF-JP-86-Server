using Dfo.Server.Protocol.Game.ItemSockets;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ItemSockets;

/// <summary>What the ItemSockets slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ItemSocketsRegistration
{
    internal static IServiceCollection AddItemSocketsSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<ItemSocketCompleted, ItemSocketCompletedHandler>();
        services.AddGameCmdMessageDecoder<ItemSocketDecoder>(0x037A, ClientMessageType.GameEquipmentSocketOpen);
        services.AddGameCmdMessageDecoder<ItemSocketDecoder>(0x0379, ClientMessageType.GameEquipmentEmblem);
        services.AddGameCmdMessageDecoder<ItemSocketDecoder>(0x00CE, ClientMessageType.GameAvatarSocketOpen);
        services.AddGameCmdMessageDecoder<ItemSocketDecoder>(0x00C9, ClientMessageType.GameAvatarEmblem);
        services.AddGameCmdMessageEncoder<ItemSocketResponse, ItemSocketEncoder>();
        services.AddProtocolCommandHandler<GameItemSocketCommand, ItemSocketResult, GameItemSocketCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameAvatarEmblem, typeof(ItemSocketRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameAvatarSocketOpen, typeof(ItemSocketRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameEquipmentEmblem, typeof(ItemSocketRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameEquipmentSocketOpen, typeof(ItemSocketRequest)));
        services.RegisterProtocolMessageHandler<ItemSocketHandler, ItemSocketRequest, GameItemSocketCommand>(ClientMessageType.GameEquipmentSocketOpen);
        services.RegisterProtocolMessageHandler<ItemSocketHandler, ItemSocketRequest, GameItemSocketCommand>(ClientMessageType.GameEquipmentEmblem);
        services.RegisterProtocolMessageHandler<ItemSocketHandler, ItemSocketRequest, GameItemSocketCommand>(ClientMessageType.GameAvatarSocketOpen);
        services.RegisterProtocolMessageHandler<ItemSocketHandler, ItemSocketRequest, GameItemSocketCommand>(ClientMessageType.GameAvatarEmblem);
        return services;
    }
}
