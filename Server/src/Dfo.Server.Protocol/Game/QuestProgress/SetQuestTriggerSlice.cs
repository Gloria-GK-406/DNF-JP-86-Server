using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestProgress;

internal sealed class SetQuestTriggerMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        SetQuestTriggerPayloadCodec.Decode(metadata, body);
}

internal sealed class SetQuestTriggerAcknowledgementEncoder
    : GameCmdMessageEncoder<SetQuestTriggerAcknowledgement>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        SetQuestTriggerAcknowledgement message) =>
        OnePayload(message,
            SetQuestTriggerPayloadCodec.Encode(message));
}
