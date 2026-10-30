using Dfo.Server.Protocol.Game.Warehouse;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Warehouse;

/// <summary>What the Warehouse slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class WarehouseRegistration
{
    internal static IServiceCollection AddWarehouseSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<WarehouseChanged, WarehouseChangedHandler>();
        services.AddGameCmdMessageEncoder<WarehouseResponse, WarehouseResponseEncoder>();
        services.AddProtocolCommandHandler<GameWarehouseCommand, InventoryExpansionResult, GameWarehouseCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameCreateAccountCargo, typeof(WarehouseRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameUpgradeAccountCargo, typeof(WarehouseRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameDepositAccountCargo, typeof(WarehouseRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameWithdrawAccountCargo, typeof(WarehouseRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameUpgradeCargo, typeof(WarehouseRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameUpgradeInventory, typeof(WarehouseRequest)));
        services.RegisterProtocolMessageHandler<WarehouseHandler, WarehouseRequest, GameWarehouseCommand>(ClientMessageType.GameCreateAccountCargo);
        services.AddGameCmdMessageDecoder<WarehouseDecoder>(0x0131, ClientMessageType.GameCreateAccountCargo);
        services.RegisterProtocolMessageHandler<WarehouseHandler, WarehouseRequest, GameWarehouseCommand>(ClientMessageType.GameUpgradeAccountCargo);
        services.AddGameCmdMessageDecoder<WarehouseDecoder>(0x0132, ClientMessageType.GameUpgradeAccountCargo);
        services.RegisterProtocolMessageHandler<WarehouseHandler, WarehouseRequest, GameWarehouseCommand>(ClientMessageType.GameDepositAccountCargo);
        services.AddGameCmdMessageDecoder<WarehouseDecoder>(0x0133, ClientMessageType.GameDepositAccountCargo);
        services.RegisterProtocolMessageHandler<WarehouseHandler, WarehouseRequest, GameWarehouseCommand>(ClientMessageType.GameWithdrawAccountCargo);
        services.AddGameCmdMessageDecoder<WarehouseDecoder>(0x0134, ClientMessageType.GameWithdrawAccountCargo);
        services.RegisterProtocolMessageHandler<WarehouseHandler, WarehouseRequest, GameWarehouseCommand>(ClientMessageType.GameUpgradeCargo);
        services.AddGameCmdMessageDecoder<WarehouseDecoder>(0x0198, ClientMessageType.GameUpgradeCargo);
        services.RegisterProtocolMessageHandler<WarehouseHandler, WarehouseRequest, GameWarehouseCommand>(ClientMessageType.GameUpgradeInventory);
        services.AddGameCmdMessageDecoder<WarehouseDecoder>(0x01AF, ClientMessageType.GameUpgradeInventory);
        return services;
    }
}
