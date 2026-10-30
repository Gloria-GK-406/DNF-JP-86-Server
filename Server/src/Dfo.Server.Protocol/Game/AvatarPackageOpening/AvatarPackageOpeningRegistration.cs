using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.AvatarPackageOpening;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.AvatarPackageOpening;

/// <summary>What the AvatarPackageOpening slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class AvatarPackageOpeningRegistration
{
    internal static IServiceCollection AddAvatarPackageOpeningSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<AvatarPackageOpeningDecoder>(ClientMessageType.GameAvatarPackageOpening);
        services.AddGameCmdMessageEncoder<AvatarPackageOpeningResponse, AvatarPackageOpeningEncoder>();
        services.AddProtocolCommandHandler<GameAvatarPackageOpeningCommand, PackageOpeningResult, GameAvatarPackageOpeningCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameAvatarPackageOpening, typeof(AvatarPackageOpeningRequest)));
        services.RegisterProtocolMessageHandler<AvatarPackageOpeningHandler, AvatarPackageOpeningRequest, GameAvatarPackageOpeningCommand>(ClientMessageType.GameAvatarPackageOpening);
        return services;
    }
}
