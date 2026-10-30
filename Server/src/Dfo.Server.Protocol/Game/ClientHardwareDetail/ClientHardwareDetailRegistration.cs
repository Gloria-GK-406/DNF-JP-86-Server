using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ClientHardwareDetail;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ClientHardwareDetail;

/// <summary>What the ClientHardwareDetail slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ClientHardwareDetailRegistration
{
    internal static IServiceCollection AddClientHardwareDetailSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<ClientHardwareDetailMessageDecoder>(
            GameProtocol.ClientHardwareDetailType, ClientMessageType.GameClientHardwareDetail);
        services.AddProtocolCommandHandler<GameClientHardwareDetailCommand, bool, GameClientHardwareDetailCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameClientHardwareDetail, typeof(ClientHardwareDetailReportRequest)));
        services.RegisterProtocolMessageHandler<ClientHardwareDetailHandler, ClientHardwareDetailReportRequest, GameClientHardwareDetailCommand>(ClientMessageType.GameClientHardwareDetail);
        return services;
    }
}
