using System.Text;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Framing.Encoders;

internal sealed class ChannelConnectResponseEncoder : ServerMessageEncoder<ChannelConnectResponse>
{
    protected override IReadOnlyList<byte[]> EncodeMessage(ChannelConnectResponse message)
    {
        var keyBytes = Encoding.ASCII.GetBytes(message.EncryptionKey);
        if (keyBytes.Length > 32)
        {
            throw new InvalidOperationException("The channel connect key cannot exceed 32 ASCII bytes.");
        }

        var body = new byte[36];
        keyBytes.CopyTo(body, 4);
        return One(ChannelFrameEncoder.Encode(
            classification: 0x7C,
            messageNumber: 0x0C,
            checksum: 0,
            acknowledgement: 1,
            body));
    }
}
