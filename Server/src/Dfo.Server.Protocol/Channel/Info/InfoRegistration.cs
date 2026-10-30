using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Channel;
using Dfo.Server.Protocol.Channel.Info;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Channel.Info;

/// <summary>What the Info slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class InfoRegistration
{
    internal static IServiceCollection AddInfoSlice(this IServiceCollection services)
    {
        services.AddChannelMessageDecoder<ChannelInfoMessageDecoder>(
            ChannelProtocol.InfoMessageNumber, ClientMessageType.ChannelInfo);
        services.AddServerMessageEncoder<ChannelInfoResponse, ChannelInfoResponseEncoder>(
            NetworkEndpointKind.Channel);
        services.AddProtocolCommandHandler<ChannelInfoCommand, ChannelInfoCompleted, ChannelInfoCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.ChannelInfo, typeof(ChannelInfoRequest)));
        services.RegisterProtocolMessageHandler<ChannelInfoHandler, ChannelInfoRequest, ChannelInfoCommand>(ClientMessageType.ChannelInfo);
        return services;
    }
}
