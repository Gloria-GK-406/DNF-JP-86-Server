using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.SecretShop;

internal static class SecretShopRegistration
{
    internal static IServiceCollection AddSecretShopSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<SecretShopBuyDecoder>(ClientMessageType.GameSecretShopBuy);
        services.AddGameCmdMessageDecoder<SecretShopWindowDecoder>(ClientMessageType.GameSecretShopWindow);
        services.AddGameCmdMessageEncoder<SecretShopBuyResponse, SecretShopBuyEncoder>();
        services.AddGameNotiMessageEncoder<SecretShopItemsNotification, SecretShopItemsEncoder>();
        services.AddProtocolCommandHandler<GameSecretShopBuyCommand, SecretShopPurchaseResult, GameSecretShopBuyCommandHandler>(
            [CommandRegistrationExit.CommittedReplayCurrentStateBatch],
            new ProtocolCommandMessageBinding(ClientMessageType.GameSecretShopBuy, typeof(SecretShopBuyRequest)));
        services.RegisterProtocolMessageHandler<SecretShopBuyHandler, SecretShopBuyRequest, GameSecretShopBuyCommand>(
            ClientMessageType.GameSecretShopBuy);
        services.RegisterTelemetryMessageHandler<SecretShopWindowHandler>(ClientMessageType.GameSecretShopWindow);
        return services;
    }
}
