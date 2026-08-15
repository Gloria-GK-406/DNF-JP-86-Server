using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Protocol.Channel;

/// <summary>
/// The Channel envelope shared by every encrypted Channel response: cipher the plaintext with the
/// day key, then wrap it in a Channel frame. Classification and acknowledgement are the observed
/// constants for server-to-client Channel traffic.
/// </summary>
internal static class ChannelEncryptedFrame
{
    public static byte[] Encode(
        byte messageNumber,
        ReadOnlySpan<byte> plaintext,
        string key,
        bool compress)
    {
        var body = ChannelPayloadCipher.Encrypt(plaintext, key, compress);
        return ChannelFrameEncoder.Encode(0x7C, messageNumber, 0, 1, body);
    }
}
