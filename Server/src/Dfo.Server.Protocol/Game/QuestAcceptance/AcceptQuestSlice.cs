using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestAcceptance;

internal sealed class AcceptQuestMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        AcceptQuestPayloadCodec.Decode(metadata, body);
}

internal sealed class AcceptQuestAcknowledgementEncoder
    : GameCmdMessageEncoder<AcceptQuestAcknowledgement>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        AcceptQuestAcknowledgement message) =>
        OnePayload(message,
            AcceptQuestPayloadCodec.Encode(message));
}
