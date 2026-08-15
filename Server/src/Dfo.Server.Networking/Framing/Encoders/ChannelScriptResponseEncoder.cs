using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Framing.Encoders;

internal sealed class ChannelScriptResponseEncoder(IChannelEncryptionKeyProvider keyProvider)
    : ServerMessageEncoder<ChannelScriptResponse>
{
    protected override IReadOnlyList<byte[]> EncodeMessage(ChannelScriptResponse message) =>
        One(ChannelEncryptedFrame.Encode(
            0x0A, message.Content, keyProvider.GetCurrentKey(), compress: true));
}
