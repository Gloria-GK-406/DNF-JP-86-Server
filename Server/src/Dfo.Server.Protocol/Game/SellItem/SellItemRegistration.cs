using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.SellItem;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.SellItem;

/// <summary>What the SellItem slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class SellItemRegistration
{
    internal static IServiceCollection AddSellItemSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<ItemSaleCompleted, ItemSaleCompletedHandler>();
        services.AddGameCmdMessageDecoder<SellItemMessageDecoder>(ClientMessageType.GameSellItem);
        services.AddGameCmdMessageEncoder<SellItemResponse, SellItemResponseEncoder>();
        services.AddProtocolCommandHandler<GameSellItemCommand, bool, GameSellItemCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSellItem, typeof(SellItemRequest)));
        services.RegisterProtocolMessageHandler<SellItemHandler, SellItemRequest, GameSellItemCommand>(ClientMessageType.GameSellItem);
        return services;
    }
}
