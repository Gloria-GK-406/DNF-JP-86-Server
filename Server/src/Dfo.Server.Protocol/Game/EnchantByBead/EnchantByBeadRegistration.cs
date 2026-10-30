using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.EnchantByBead;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.EnchantByBead;

/// <summary>What the EnchantByBead slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class EnchantByBeadRegistration
{
    internal static IServiceCollection AddEnchantByBeadSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<EquipmentEnchantCompleted,
            EquipmentEnchantCompletedHandler>();
        services.AddProtocolCommandHandler<GameEnchantByBeadCommand, EquipmentEnchantResult, GameEnchantByBeadCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameEnchantByBead, typeof(EnchantByBeadRequest)));
        services.AddGameCmdMessageDecoder<EnchantByBeadDecoder>(ClientMessageType.GameEnchantByBead);
        services.AddGameCmdMessageEncoder<EnchantByBeadResponse, EnchantByBeadEncoder>();
        services.RegisterProtocolMessageHandler<EnchantByBeadHandler, EnchantByBeadRequest, GameEnchantByBeadCommand>(ClientMessageType.GameEnchantByBead);
        return services;
    }
}
