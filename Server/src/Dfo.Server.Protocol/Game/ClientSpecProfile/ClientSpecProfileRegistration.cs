using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ClientSpecProfile;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ClientSpecProfile;

/// <summary>What the ClientSpecProfile slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ClientSpecProfileRegistration
{
    internal static IServiceCollection AddClientSpecProfileSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<ClientSpecProfileMessageDecoder>(
            GameProtocol.ClientSpecProfileType, ClientMessageType.GameClientSpecProfile);
        services.AddProtocolCommandHandler<GameClientSpecProfileCommand, bool, GameClientSpecProfileCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameClientSpecProfile, typeof(ClientSpecProfileReportRequest)));
        services.RegisterProtocolMessageHandler<ClientSpecProfileHandler, ClientSpecProfileReportRequest, GameClientSpecProfileCommand>(ClientMessageType.GameClientSpecProfile);
        return services;
    }
}
