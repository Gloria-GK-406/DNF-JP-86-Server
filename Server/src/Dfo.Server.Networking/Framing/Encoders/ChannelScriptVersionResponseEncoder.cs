using System.Text;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Framing.Encoders;

internal sealed class ChannelScriptVersionResponseEncoder(IChannelEncryptionKeyProvider keyProvider)
    : ServerMessageEncoder<ChannelScriptVersionResponse>
{
    protected override IReadOnlyList<byte[]> EncodeMessage(ChannelScriptVersionResponse message)
    {
        var versionBytes = Encoding.ASCII.GetBytes(message.Version);
        if (versionBytes.Length > 16)
        {
            throw new InvalidOperationException("The channel script version cannot exceed 16 ASCII bytes.");
        }

        var plaintext = new byte[20];
        versionBytes.CopyTo(plaintext, 4);
        return One(ChannelEncryptedFrame.Encode(
            0x06, plaintext, keyProvider.GetCurrentKey(), compress: false));
    }
}
