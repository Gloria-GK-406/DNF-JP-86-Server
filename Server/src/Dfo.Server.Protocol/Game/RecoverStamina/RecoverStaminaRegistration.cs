using Dfo.Server.Protocol.Game.RecoverStamina;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.RecoverStamina;

/// <summary>What the RecoverStamina slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class RecoverStaminaRegistration
{
    internal static IServiceCollection AddRecoverStaminaSlice(this IServiceCollection services)
    {
        services.AddProtocolCommandHandler<GameRecoverStaminaCommand, RecoverStaminaResult, GameRecoverStaminaCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRecoverStamina, typeof(RecoverStaminaRequest)));
        services.AddNotificationHandler<StaminaRecovered, StaminaRecoveredHandler>();
        services.AddGameCmdMessageDecoder<RecoverStaminaDecoder>(ClientMessageType.GameRecoverStamina);
        services.AddGameCmdMessageEncoder<RecoverStaminaResponse, RecoverStaminaEncoder>();
        services.RegisterProtocolMessageHandler<RecoverStaminaHandler, RecoverStaminaRequest, GameRecoverStaminaCommand>(ClientMessageType.GameRecoverStamina);
        return services;
    }
}
