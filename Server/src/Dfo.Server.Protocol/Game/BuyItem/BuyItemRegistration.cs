using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.BuyItem;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.BuyItem;

/// <summary>What the BuyItem slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class BuyItemRegistration
{
    internal static IServiceCollection AddBuyItemSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<NpcShopPurchaseCompleted, NpcShopPurchaseCompletedHandler>();
        services.AddGameCmdMessageDecoder<BuyItemMessageDecoder>(
            GameProtocol.BuyItemType, ClientMessageType.GameBuyItem);
        services.AddGameCmdMessageEncoder<BuyItemResponse, BuyItemResponseEncoder>();
        services.AddProtocolCommandHandler<GameBuyItemCommand, BuyItemOutcome, GameBuyItemCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameBuyItem, typeof(BuyItemRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameBuyItem, typeof(HousingBuyItemRequest)));
        services.RegisterProtocolMessageHandler<BuyItemHandler, BuyItemRequest, GameBuyItemCommand>(ClientMessageType.GameBuyItem,
            typeof(BuyItemRequest), typeof(HousingBuyItemRequest));
        return services;
    }
}
