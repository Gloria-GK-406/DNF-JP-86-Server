using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Framing.Encoders;

internal sealed class ChannelInfoResponseEncoder(IChannelEncryptionKeyProvider keyProvider)
    : ServerMessageEncoder<ChannelInfoResponse>
{
    protected override IReadOnlyList<byte[]> EncodeMessage(ChannelInfoResponse message)
    {
        var plaintext = new byte[checked(6 + message.Channels.Count * 48)];
        BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(2, 4), message.Channels.Count);
        var offset = 6;
        foreach (var channel in message.Channels)
        {
            WriteFixedAscii(plaintext.AsSpan(offset, 20), channel.Name);
            BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(offset + 20, 4), channel.MaximumUsers);
            BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(offset + 24, 4), channel.CurrentUsers);
            WriteFixedAscii(plaintext.AsSpan(offset + 28, 16), channel.Host);
            BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(offset + 44, 4), channel.Port);
            offset += 48;
        }

        return One(ChannelEncryptedFrame.Encode(
            0x12, plaintext, keyProvider.GetCurrentKey(), compress: true));
    }

    private static void WriteFixedAscii(Span<byte> target, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
        bytes.AsSpan(0, Math.Min(bytes.Length, target.Length)).CopyTo(target);
    }
}
