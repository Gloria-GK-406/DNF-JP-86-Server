using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.EpicCraft;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.EpicCraft;

/// <summary>What the EpicCraft slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class EpicCraftRegistration
{
    internal static IServiceCollection AddEpicCraftSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<EpicCraftCompleted, EpicCraftCompletedHandler>();
        services.AddGameCmdMessageDecoder<EpicCraftDecoder>(GameProtocol.EpicCraftType, ClientMessageType.GameEpicCraft);
        services.AddGameCmdMessageEncoder<EpicCraftResponse, EpicCraftEncoder>();
        services.AddProtocolCommandHandler<GameEpicCraftCommand, bool, GameEpicCraftCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameEpicCraft, typeof(EpicCraftRequest)));
        services.RegisterProtocolMessageHandler<EpicCraftHandler, EpicCraftRequest, GameEpicCraftCommand>(ClientMessageType.GameEpicCraft);
        return services;
    }
}
