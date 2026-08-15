using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ClientHardwareDetail;

internal sealed class ClientHardwareDetailMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        ClientHardwareDetailPayloadCodec.Decode(metadata, body);
}
