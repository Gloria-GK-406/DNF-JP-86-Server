namespace Dfo.Server.Protocol.Game.CardReward;

/// <summary>
/// The layout frame, under CMD 0x0046.
/// </summary>
/// <remarks>
/// The message it encodes carries nothing, so neither does the frame: the body is the constant in
/// <see cref="CardRewardPayloadCodec.EncodeLayout"/> and there is no path by which a card's
/// contents could reach it.
/// </remarks>
internal sealed class CardSelectRightStateResponseEncoder
    : GameCmdMessageEncoder<CardSelectRightStateResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        CardSelectRightStateResponse message) =>
        OnePayload(GameProtocol.CardSelectRightStateType, CardRewardPayloadCodec.EncodeLayout());
}

/// <summary>The flipped face, every position of it, under CMD 0x0047.</summary>
internal sealed class SelectCardResponseEncoder : GameCmdMessageEncoder<SelectCardResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        SelectCardResponse message) =>
        OnePayload(
            GameProtocol.SelectCardType,
            CardRewardPayloadCodec.EncodeSelectCardResponse(message));
}
