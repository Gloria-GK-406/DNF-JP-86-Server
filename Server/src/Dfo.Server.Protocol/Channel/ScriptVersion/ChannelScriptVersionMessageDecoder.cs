using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.ScriptVersion;

internal sealed class ChannelScriptVersionMessageDecoder : IChannelMessageDecoder
{
    public IClientNetworkMessage? Decode(ChannelMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new ChannelScriptVersionRequest(metadata);
}
