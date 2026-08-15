using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Framing.Decoders;

/// <summary>
/// Fronts <see cref="GetUserInfoPayloadCodec"/>, the one decoder whose wire key does not determine
/// its message type: type 0x0008 yields the account roster request only for a specific mode and
/// target combination, and yields nothing otherwise. It is the standing proof that the wire key
/// space and the message type space have to stay separate.
/// </summary>
internal sealed class GetUserInfoMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        GetUserInfoPayloadCodec.Decode(metadata, body);
}
