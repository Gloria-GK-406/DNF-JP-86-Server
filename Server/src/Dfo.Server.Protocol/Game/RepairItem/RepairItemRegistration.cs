using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.RepairItem;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.RepairItem;

/// <summary>What the RepairItem slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class RepairItemRegistration
{
    internal static IServiceCollection AddRepairItemSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<EquipmentRepairCompleted, EquipmentRepairCompletedHandler>();
        services.AddGameCmdMessageDecoder<RepairItemMessageDecoder>(ClientMessageType.GameRepairItem);
        services.AddGameCmdMessageEncoder<RepairItemResponse, RepairItemResponseEncoder>();
        services.AddProtocolCommandHandler<GameRepairItemCommand, bool, GameRepairItemCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRepairItem, typeof(RepairItemRequest)));
        services.RegisterProtocolMessageHandler<RepairItemHandler, RepairItemRequest, GameRepairItemCommand>(ClientMessageType.GameRepairItem);
        return services;
    }
}
