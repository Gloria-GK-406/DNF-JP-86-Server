using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Exit;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Exit;

/// <summary>What the Exit slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ExitRegistration
{
    internal static IServiceCollection AddExitSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GameExitMessageDecoder>(
            GameProtocol.ExitType, ClientMessageType.GameExit);
        services.AddGameCmdMessageEncoder<GameExitResponse, GameExitResponseEncoder>();
        services.AddProtocolCommandHandler<GameExitCommand, bool, GameExitCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameExit, typeof(GameExitRequest)));
        services.RegisterProtocolMessageHandler<GameExitHandler, GameExitRequest, GameExitCommand>(ClientMessageType.GameExit);
        return services;
    }
}
