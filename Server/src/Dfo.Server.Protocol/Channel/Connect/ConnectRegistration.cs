using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Channel;
using Dfo.Server.Protocol.Channel.Connect;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Channel.Connect;

/// <summary>What the Connect slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ConnectRegistration
{
    internal static IServiceCollection AddConnectSlice(this IServiceCollection services)
    {
        services.AddChannelMessageDecoder<ChannelConnectMessageDecoder>(
            ChannelProtocol.ConnectMessageNumber, ClientMessageType.ChannelConnect);
        services.AddServerMessageEncoder<ChannelConnectResponse, ChannelConnectResponseEncoder>(
            NetworkEndpointKind.Channel);

        services.AddProtocolCommandHandler<ChannelConnectCommand, ChannelConnectCompleted, ChannelConnectCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.ChannelConnect, typeof(ChannelConnectRequest)));

        services.RegisterProtocolMessageHandler<ChannelConnectHandler, ChannelConnectRequest, ChannelConnectCommand>(ClientMessageType.ChannelConnect);
        return services;
    }
}
