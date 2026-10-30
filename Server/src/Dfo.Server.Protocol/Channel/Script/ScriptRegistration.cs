using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Channel;
using Dfo.Server.Protocol.Channel.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Channel.Script;

/// <summary>What the Script slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ScriptRegistration
{
    internal static IServiceCollection AddScriptSlice(this IServiceCollection services)
    {
        services.AddChannelMessageDecoder<ChannelScriptMessageDecoder>(
            ChannelProtocol.ScriptMessageNumber, ClientMessageType.ChannelScript);
        services.AddServerMessageEncoder<ChannelScriptResponse, ChannelScriptResponseEncoder>(
            NetworkEndpointKind.Channel);
        services.AddProtocolCommandHandler<ChannelScriptCommand, ChannelScriptCompleted, ChannelScriptCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.ChannelScript, typeof(ChannelScriptRequest)));
        services.RegisterProtocolMessageHandler<ChannelScriptHandler, ChannelScriptRequest, ChannelScriptCommand>(ClientMessageType.ChannelScript);
        return services;
    }
}
