namespace Dfo.Server.Protocol.Game.CardReward;

/// <summary>
/// The layout frame, under CMD 0x0046.
/// </summary>
/// <remarks>
/// The message carries eligible party seats but no reward contents. The body remains an eight-word
/// layout; see <see cref="CardRewardPayloadCodec.EncodeLayout"/>.
/// </remarks>
internal sealed class CardSelectRightStateResponseEncoder
    : GameCmdMessageEncoder<CardSelectRightStateResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        CardSelectRightStateResponse message) =>
        OnePayload(
            GameProtocol.CardSelectRightStateType,
            CardRewardPayloadCodec.EncodeLayout(message.EligibleSeats));
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
