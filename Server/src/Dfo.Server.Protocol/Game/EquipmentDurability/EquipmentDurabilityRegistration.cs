using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.EquipmentDurability;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.EquipmentDurability;

/// <summary>What the EquipmentDurability slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class EquipmentDurabilityRegistration
{
    internal static IServiceCollection AddEquipmentDurabilitySlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<EquipmentDurabilityChanged, EquipmentDurabilityChangedHandler>();
        services.AddGameCmdMessageDecoder<DecreaseDurabilityMessageDecoder>(
            GameProtocol.DecreaseDurabilityType, ClientMessageType.GameDecreaseDurability);
        services.AddGameCmdMessageEncoder<DecreaseDurabilityResponse, DecreaseDurabilityResponseEncoder>();
        services.AddProtocolCommandHandler<GameDecreaseDurabilityCommand, bool, GameDecreaseDurabilityCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameDecreaseDurability, typeof(DecreaseDurabilityRequest)));
        services.RegisterProtocolMessageHandler<DecreaseDurabilityHandler, DecreaseDurabilityRequest, GameDecreaseDurabilityCommand>(ClientMessageType.GameDecreaseDurability);
        return services;
    }
}
