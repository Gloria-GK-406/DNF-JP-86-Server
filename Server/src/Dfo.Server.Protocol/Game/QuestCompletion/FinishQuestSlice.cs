using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestCompletion;

internal sealed class FinishQuestMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        FinishQuestPayloadCodec.Decode(metadata, body);
}

internal sealed class FinishQuestAcknowledgementEncoder
    : GameCmdMessageEncoder<FinishQuestAcknowledgement>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        FinishQuestAcknowledgement message) =>
        OnePayload(
            GameProtocol.FinishQuestType,
            FinishQuestPayloadCodec.Encode(message));
}
