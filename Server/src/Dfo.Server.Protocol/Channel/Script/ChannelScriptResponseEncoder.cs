using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Script;

internal sealed class ChannelScriptResponseEncoder(ChannelProtocolKeyProvider keyProvider)
    : ServerMessageEncoder<ChannelScriptResponse>
{
    protected override IReadOnlyList<byte[]> EncodeMessage(ChannelScriptResponse message) =>
        One(ChannelEncryptedFrame.Encode(
            0x0A, message.Content, keyProvider.GetCurrentKey(), compress: true));
}
