using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Info;

internal sealed class ChannelInfoMessageDecoder : IChannelMessageDecoder
{
    public IClientNetworkMessage? Decode(ChannelMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new ChannelInfoRequest(metadata);
}
