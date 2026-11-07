using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.UpgradeChronicle;

/// <summary>What the UpgradeChronicle slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class UpgradeChronicleRegistration
{
    internal static IServiceCollection AddUpgradeChronicleSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<EquipmentEmancipateCompleted, EquipmentEmancipateCompletedHandler>();
        services.AddGameCmdMessageDecoder<UpgradeChronicleDecoder>(ClientMessageType.GameUpgradeChronicle);
        services.AddGameCmdMessageEncoder<UpgradeChronicleResponse, UpgradeChronicleEncoder>();
        services.AddProtocolCommandHandler<GameUpgradeChronicleCommand, EquipmentEmancipateResult, GameUpgradeChronicleCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameUpgradeChronicle, typeof(UpgradeChronicleRequest)));
        services.RegisterProtocolMessageHandler<UpgradeChronicleHandler, UpgradeChronicleRequest, GameUpgradeChronicleCommand>(
            ClientMessageType.GameUpgradeChronicle);
        return services;
    }
}
