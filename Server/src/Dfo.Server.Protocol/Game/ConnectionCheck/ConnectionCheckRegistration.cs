using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ConnectionCheck;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ConnectionCheck;

/// <summary>What the ConnectionCheck slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ConnectionCheckRegistration
{
    internal static IServiceCollection AddConnectionCheckSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<ConnectionCheckMessageDecoder>(
            GameProtocol.ConnectionCheckType, ClientMessageType.GameConnectionCheck);
        services.AddGameCmdMessageEncoder<ConnectionCheckResponse, ConnectionCheckResponseEncoder>();
        services.AddProtocolCommandHandler<GameConnectionCheckCommand, bool, GameConnectionCheckCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameConnectionCheck, typeof(ConnectionCheckRequest)));
        services.RegisterProtocolMessageHandler<ConnectionCheckHandler, ConnectionCheckRequest, GameConnectionCheckCommand>(ClientMessageType.GameConnectionCheck);
        return services;
    }
}
