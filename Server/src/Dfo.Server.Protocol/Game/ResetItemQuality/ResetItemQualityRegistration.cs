using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ResetItemQuality;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ResetItemQuality;

/// <summary>What the ResetItemQuality slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ResetItemQualityRegistration
{
    internal static IServiceCollection AddResetItemQualitySlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<EquipmentQualityCompleted, EquipmentQualityCompletedHandler>();
        services.AddGameCmdMessageDecoder<ResetItemQualityDecoder>(ClientMessageType.GameResetItemQuality);
        services.AddGameCmdMessageEncoder<ResetItemQualityResponse, ResetItemQualityEncoder>();
        services.AddProtocolCommandHandler<GameResetItemQualityCommand, EquipmentQualityResult, GameResetItemQualityCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameResetItemQuality, typeof(ResetItemQualityRequest)));
        services.RegisterProtocolMessageHandler<ResetItemQualityHandler, ResetItemQualityRequest, GameResetItemQualityCommand>(ClientMessageType.GameResetItemQuality);
        return services;
    }
}
