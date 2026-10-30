using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.FinishLoading;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.FinishLoading;

/// <summary>What the FinishLoading slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class FinishLoadingRegistration
{
    internal static IServiceCollection AddFinishLoadingSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<FinishLoadingDecoder>(
            GameProtocol.FinishLoadingType, ClientMessageType.GameFinishLoading);
        services.AddGameCmdMessageEncoder<FinishLoadingResponse, FinishLoadingResponseEncoder>();
        services.AddGameNotiMessageEncoder<LoadingCompleteNotification, LoadingCompleteEncoder>();
        services.AddProtocolCommandHandler<GameFinishLoadingCommand, bool, GameMapLoadingCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameFinishLoading, typeof(FinishLoadingRequest)));
        services.RegisterProtocolMessageHandler<FinishLoadingHandler, FinishLoadingRequest, GameFinishLoadingCommand>(ClientMessageType.GameFinishLoading);
        return services;
    }
}
