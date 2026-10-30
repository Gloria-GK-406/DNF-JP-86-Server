using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.PackageOpening;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.PackageOpening;

/// <summary>What the PackageOpening slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class PackageOpeningRegistration
{
    internal static IServiceCollection AddPackageOpeningSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<PackageOpeningDecoder>(ClientMessageType.GamePackageOpening);
        services.AddGameCmdMessageEncoder<PackageOpeningResponse, PackageOpeningEncoder>();
        services.AddProtocolCommandHandler<GamePackageOpeningCommand, PackageOpeningResult, GamePackageOpeningCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GamePackageOpening, typeof(PackageOpeningRequest)));
        services.RegisterProtocolMessageHandler<PackageOpeningHandler, PackageOpeningRequest, GamePackageOpeningCommand>(ClientMessageType.GamePackageOpening);
        return services;
    }
}
