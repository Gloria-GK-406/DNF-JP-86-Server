using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ResetRandomOption;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ResetRandomOption;

/// <summary>What the ResetRandomOption slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ResetRandomOptionRegistration
{
    internal static IServiceCollection AddResetRandomOptionSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<MagicSealGradeCompleted, MagicSealGradeCompletedHandler>();
        services.AddGameCmdMessageDecoder<ResetRandomOptionDecoder>(0x01C8, ClientMessageType.GameResetRandomOption);
        services.AddGameCmdMessageEncoder<ResetRandomOptionResponse, ResetRandomOptionEncoder>();
        services.AddProtocolCommandHandler<GameResetRandomOptionCommand, EquipmentQualityResult, GameResetRandomOptionCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameResetRandomOption, typeof(ResetRandomOptionRequest)));
        services.RegisterProtocolMessageHandler<ResetRandomOptionHandler, ResetRandomOptionRequest, GameResetRandomOptionCommand>(ClientMessageType.GameResetRandomOption);
        return services;
    }
}
