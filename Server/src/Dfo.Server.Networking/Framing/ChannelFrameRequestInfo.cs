using System.Buffers.Binary;
using TouchSocket.Core;

namespace Dfo.Server.Networking.Framing;

public sealed class ChannelFrameRequestInfo : IFixedHeaderRequestInfo
{
    public const int HeaderLength = 11;

    public byte Classification { get; private set; }

    public byte MessageNumber { get; private set; }

    public uint TotalLength { get; private set; }

    public uint Checksum { get; private set; }

    public byte Acknowledgement { get; private set; }

    public int BodyLength { get; private set; }

    public ReadOnlyMemory<byte> Body { get; private set; }

    public bool OnParsingHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderLength)
        {
            return false;
        }

        Classification = header[0];
        MessageNumber = header[1];
        TotalLength = BinaryPrimitives.ReadUInt32LittleEndian(header[2..6]);
        Checksum = BinaryPrimitives.ReadUInt32LittleEndian(header[6..10]);
        Acknowledgement = header[10];
        BodyLength = DnfFrameProtocol.GetBodyLength(TotalLength, HeaderLength);
        return true;
    }

    public bool OnParsingBody(ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            return false;
        }

        Body = body.ToArray();
        return true;
    }
}
