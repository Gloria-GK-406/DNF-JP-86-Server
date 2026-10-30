using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestGiveUp;

internal sealed class GiveUpQuestMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        GiveUpQuestPayloadCodec.Decode(metadata, body);
}

internal sealed class GiveUpQuestAcknowledgementEncoder
    : GameCmdMessageEncoder<GiveUpQuestAcknowledgement>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GiveUpQuestAcknowledgement message) =>
        OnePayload(message, GiveUpQuestPayloadCodec.Encode(message));
}
