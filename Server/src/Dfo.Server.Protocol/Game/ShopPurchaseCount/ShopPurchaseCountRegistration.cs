using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ShopPurchaseCount;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ShopPurchaseCount;

/// <summary>What the ShopPurchaseCount slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ShopPurchaseCountRegistration
{
    internal static IServiceCollection AddShopPurchaseCountSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<ShopPurchaseCountsReported, ShopPurchaseCountsReportedHandler>();
        services.AddGameCmdMessageDecoder<ShopPurchaseCountDecoder>(ClientMessageType.GameShopPurchaseCount);
        services.AddProtocolCommandHandler<GameShopPurchaseCountCommand, bool, GameShopPurchaseCountCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameShopPurchaseCount, typeof(ShopPurchaseCountRequest)));
        services.RegisterProtocolMessageHandler<ShopPurchaseCountHandler, ShopPurchaseCountRequest, GameShopPurchaseCountCommand>(ClientMessageType.GameShopPurchaseCount);
        return services;
    }
}
