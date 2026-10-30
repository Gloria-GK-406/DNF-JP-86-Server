using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Channel;
using Dfo.Server.Protocol.Channel.ScriptVersion;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Channel.ScriptVersion;

/// <summary>What the ScriptVersion slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ScriptVersionRegistration
{
    internal static IServiceCollection AddScriptVersionSlice(this IServiceCollection services)
    {
        services.AddChannelMessageDecoder<ChannelScriptVersionMessageDecoder>(
            ChannelProtocol.ScriptVersionMessageNumber, ClientMessageType.ChannelScriptVersion);
        services.AddServerMessageEncoder<ChannelScriptVersionResponse, ChannelScriptVersionResponseEncoder>(
            NetworkEndpointKind.Channel);
        services.AddProtocolCommandHandler<ChannelScriptVersionCommand, ChannelScriptVersionCompleted, ChannelScriptVersionCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.ChannelScriptVersion, typeof(ChannelScriptVersionRequest)));
        services.RegisterProtocolMessageHandler<ChannelScriptVersionHandler, ChannelScriptVersionRequest, ChannelScriptVersionCommand>(ClientMessageType.ChannelScriptVersion);
        return services;
    }
}
