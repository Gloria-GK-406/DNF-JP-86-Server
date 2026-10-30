using Dfo.Server.Protocol.Game.Teleport;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Teleport;

/// <summary>What the Teleport slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class TeleportRegistration
{
    internal static IServiceCollection AddTeleportSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<TeleportDecoder>(ClientMessageType.GameTeleport);
        services.AddGameCmdMessageEncoder<TeleportResponse, TeleportEncoder>();
        services.AddGameNotiMessageEncoder<TeleportCooldownNotification, TeleportCooldownEncoder>();
        services.AddProtocolCommandHandler<GameTeleportCommand, bool, GameTeleportCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameTeleport, typeof(TeleportRequest)));
        services.RegisterProtocolMessageHandler<TeleportHandler, TeleportRequest, GameTeleportCommand>(ClientMessageType.GameTeleport);
        return services;
    }
}
