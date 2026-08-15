using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Framing.Decoders;

internal sealed class SessionMetricsMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        SessionMetricsPayloadCodec.Decode(metadata, body);
}
