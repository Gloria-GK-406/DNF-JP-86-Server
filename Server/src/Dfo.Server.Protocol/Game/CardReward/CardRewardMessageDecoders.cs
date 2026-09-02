using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.CardReward;

/// <summary>
/// CMD 0x0046: an empty body, and nothing to read out of it.
/// </summary>
/// <remarks>
/// The layout is asked for rather than described, so the frame carries no field at all -- the
/// record calls the layout closed at zero bytes. Anything that did arrive in the body is left
/// unread rather than refused: this type has no field for a longer body to disagree with.
/// </remarks>
internal sealed class CardSelectRightStateDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new CardSelectRightStateRequest(metadata);
}

/// <summary>
/// CMD 0x0047: which face, and which position of it.
/// </summary>
/// <remarks>
/// The reading lives in <see cref="CardRewardPayloadCodec.ReadSelectCardRequest"/>, where the free
/// and paid shapes are kept apart. A body this seam cannot read is declined -- null -- so the frame
/// is dropped and the connection is not.
/// </remarks>
internal sealed class SelectCardDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        CardRewardPayloadCodec.ReadSelectCardRequest(metadata, body);
}
