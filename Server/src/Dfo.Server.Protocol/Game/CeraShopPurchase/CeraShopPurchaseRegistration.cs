using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CeraShopPurchase;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.CeraShopPurchase;

/// <summary>What the CeraShopPurchase slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class CeraShopPurchaseRegistration
{
    internal static IServiceCollection AddCeraShopPurchaseSlice(this IServiceCollection services)
    {
        services.AddDomainSubscriber<Dfo.Server.Abstractions.Characters.CharacterNameTagExpired, NameTagExpiredSubscriber>(
            "character.name-tag.expiration.notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddNotificationHandler<CeraShopPurchaseCompleted, CeraShopPurchaseCompletedHandler>();
        services.AddGameCmdMessageDecoder<CeraShopPurchaseMessageDecoder>(0x0040, ClientMessageType.GameCeraShopPurchase);
        services.AddGameCmdMessageEncoder<CeraShopPurchaseResponse, CeraShopPurchaseResponseEncoder>();
        services.AddProtocolCommandHandler<GameCeraShopPurchaseCommand, CeraShopPurchaseResult, GameCeraShopPurchaseCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCeraShopPurchase, typeof(CeraShopPurchaseRequest)));
        services.RegisterProtocolMessageHandler<CeraShopPurchaseHandler, CeraShopPurchaseRequest, GameCeraShopPurchaseCommand>(ClientMessageType.GameCeraShopPurchase);
        return services;
    }
}
