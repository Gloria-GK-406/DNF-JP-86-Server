using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ReturnToSelection;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ReturnToSelection;

/// <summary>What the ReturnToSelection slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ReturnToSelectionRegistration
{
    internal static IServiceCollection AddReturnToSelectionSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<ReturnToSelectionMessageDecoder>(ClientMessageType.GameReturnToSelection);
        services.AddGameCmdMessageEncoder<ReturnToSelectionResponse, ReturnToSelectionResponseEncoder>();
        services.AddProtocolCommandHandler<GameReturnToSelectionCommand, bool, GameReturnToSelectionCommandHandler>(
            CommandSelectedSessionCleanupCapability.SelectedSessionLifecycle,
            new ProtocolCommandMessageBinding(ClientMessageType.GameReturnToSelection, typeof(ReturnToSelectionRequest)));
        services.RegisterProtocolMessageHandler<ReturnToSelectionHandler, ReturnToSelectionRequest, GameReturnToSelectionCommand>(ClientMessageType.GameReturnToSelection);
        return services;
    }
}
