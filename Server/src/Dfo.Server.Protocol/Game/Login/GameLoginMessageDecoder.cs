using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Login;

/// <summary>
/// Fronts <see cref="GameLoginPayloadCodec"/>, which is the one inbound codec that reports failure
/// through a bool and two out parameters rather than a null return.
/// </summary>
/// <remarks>
/// Its shape is deliberately left alone. Normalising it to a nullable return would mean rewriting
/// the failure path that currently decides, for a malformed login body, that the frame is dropped
/// rather than raised -- and that decision is behaviour, not shape. Assembling the message here
/// leaves the codec byte-for-byte as it is: a failed decode returns null, which is how this
/// interface says "no message", so the frame is dropped by the caller rather than raised.
/// </remarks>
internal sealed class GameLoginMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        GameLoginPayloadCodec.TryDecode(body, out var loginId, out var passwordHash)
            ? new GameLoginRequest(metadata, loginId, passwordHash)
            : null;
}
