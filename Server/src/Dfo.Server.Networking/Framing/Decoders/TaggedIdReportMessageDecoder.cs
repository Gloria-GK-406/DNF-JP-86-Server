using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Framing.Decoders;

internal sealed class TaggedIdReportMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        TaggedIdReportPayloadCodec.Decode(metadata, body);
}
