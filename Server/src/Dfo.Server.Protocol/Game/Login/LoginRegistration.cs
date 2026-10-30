using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Login;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Login;

/// <summary>What the Login slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class LoginRegistration
{
    internal static IServiceCollection AddLoginSlice(this IServiceCollection services)
    {

        services.AddGameCmdMessageDecoder<GameLoginMessageDecoder>(ClientMessageType.GameLogin);
        services.AddGameCmdMessageEncoder<GameLoginResponse, GameLoginResponseEncoder>();
        services.AddProtocolCommandHandler<GameLoginCommand, GameLoginCompleted, GameLoginCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameLogin, typeof(GameLoginRequest)));
        services.RegisterProtocolMessageHandler<GameLoginHandler, GameLoginRequest, GameLoginCommand>(ClientMessageType.GameLogin);
        return services;
    }
}
