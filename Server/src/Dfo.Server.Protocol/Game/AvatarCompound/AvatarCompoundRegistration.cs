using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.AvatarCompound;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.AvatarCompound;

/// <summary>What the AvatarCompound slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class AvatarCompoundRegistration
{
    internal static IServiceCollection AddAvatarCompoundSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<AvatarCompoundCompleted, AvatarCompoundCompletedHandler>();
        services.AddGameCmdMessageDecoder<AvatarCompoundDecoder>(ClientMessageType.GameAvatarCompound);
        services.AddGameCmdMessageEncoder<AvatarCompoundResponse, AvatarCompoundEncoder>();
        services.AddProtocolCommandHandler<GameAvatarCompoundCommand, AvatarCompoundResult, GameAvatarCompoundCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameAvatarCompound, typeof(AvatarCompoundRequest)));
        services.RegisterProtocolMessageHandler<AvatarCompoundHandler, AvatarCompoundRequest, GameAvatarCompoundCommand>(ClientMessageType.GameAvatarCompound);
        return services;
    }
}
