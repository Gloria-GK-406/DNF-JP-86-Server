using System.Buffers.Binary;

namespace Dfo.Server.Networking.Framing;

public static class ChannelFrameEncoder
{
    public static byte[] Encode(
        byte classification,
        byte messageNumber,
        uint checksum,
        byte acknowledgement,
        ReadOnlySpan<byte> body)
    {
        var totalLength = checked(ChannelFrameRequestInfo.HeaderLength + body.Length);
        if (totalLength > DnfFrameProtocol.MaximumFrameLength)
        {
            throw new ArgumentOutOfRangeException(nameof(body), body.Length, "Frame exceeds the configured safety limit.");
        }

        var frame = new byte[totalLength];
        frame[0] = classification;
        frame[1] = messageNumber;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(2), (uint)totalLength);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(6), checksum);
        frame[10] = acknowledgement;
        body.CopyTo(frame.AsSpan(ChannelFrameRequestInfo.HeaderLength));
        return frame;
    }
}

public static class GameServerFrameEncoder
{
    public const int HeaderLength = 15;

    public static byte[] Encode(
        byte selector,
        ushort type,
        ReadOnlySpan<byte> body,
        uint checksum = 0,
        uint reserved = 0)
    {
        var totalLength = checked(HeaderLength + body.Length);
        if (totalLength > DnfFrameProtocol.MaximumFrameLength)
        {
            throw new ArgumentOutOfRangeException(nameof(body), body.Length, "Frame exceeds the configured safety limit.");
        }

        var frame = new byte[totalLength];
        frame[0] = selector;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)totalLength);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7), checksum);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(11), reserved);
        body.CopyTo(frame.AsSpan(HeaderLength));
        return frame;
    }
}
