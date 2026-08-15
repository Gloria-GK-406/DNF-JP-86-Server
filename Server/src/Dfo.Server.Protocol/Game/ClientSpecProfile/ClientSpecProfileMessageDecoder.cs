using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ClientSpecProfile;

internal sealed class ClientSpecProfileMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        ClientSpecProfilePayloadCodec.Decode(metadata, body);
}
