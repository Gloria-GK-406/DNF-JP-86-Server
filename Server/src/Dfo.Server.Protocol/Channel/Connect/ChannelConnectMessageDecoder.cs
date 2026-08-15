using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Connect;

internal sealed class ChannelConnectMessageDecoder : IChannelMessageDecoder
{
    public IClientNetworkMessage? Decode(ChannelMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new ChannelConnectRequest(metadata);
}
