using System.Buffers.Binary;
using TouchSocket.Core;

namespace Dfo.Server.Networking.Framing;

public sealed class GameClientFrameRequestInfo : IFixedHeaderRequestInfo
{
    public const int HeaderLength = 13;

    public byte Command { get; private set; }

    public ushort Type { get; private set; }

    public uint TotalLength { get; private set; }

    public uint Checksum { get; private set; }

    public ushort Sequence { get; private set; }

    public int BodyLength { get; private set; }

    public ReadOnlyMemory<byte> Body { get; private set; }

    public bool OnParsingHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderLength)
        {
            return false;
        }

        Command = header[0];
        Type = BinaryPrimitives.ReadUInt16LittleEndian(header[1..3]);
        TotalLength = BinaryPrimitives.ReadUInt32LittleEndian(header[3..7]);
        Checksum = BinaryPrimitives.ReadUInt32LittleEndian(header[7..11]);
        Sequence = BinaryPrimitives.ReadUInt16LittleEndian(header[11..13]);
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
