using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DeleteItem;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.DeleteItem;

/// <summary>What the DeleteItem slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class DeleteItemRegistration
{
    internal static IServiceCollection AddDeleteItemSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<ItemDiscardCompleted, ItemDiscardCompletedHandler>();
        services.AddNotificationHandler<MaterialsConsumed, MaterialsConsumedHandler>();
        services.AddGameCmdMessageDecoder<DeleteItemMessageDecoder>(
            GameProtocol.DeleteItemType, ClientMessageType.GameDeleteItem);
        services.AddGameCmdMessageEncoder<DeleteItemResponse, DeleteItemResponseEncoder>();
        services.AddProtocolCommandHandler<GameDeleteItemCommand, bool, GameDeleteItemCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameDeleteItem, typeof(DeleteItemRequest)));
        services.RegisterProtocolMessageHandler<DeleteItemHandler, DeleteItemRequest, GameDeleteItemCommand>(ClientMessageType.GameDeleteItem);
        return services;
    }
}
