using System.Buffers.Binary;
using TouchSocket.Core;

namespace Dfo.Server.Networking.Framing;

public sealed class GameClientFrameRequestInfo : IFixedHeaderRequestInfo
{
    /// <summary>
    /// The inbound Game header is fourteen bytes on the A21 client.
    /// </summary>
    /// <remarks>
    /// Measured, not ported. Every inbound frame of a known-good A21 session carries
    /// <c>selector, type, total length, checksum</c> in the first eleven bytes and three more
    /// after them; the client's own connection-check command arrives as a fourteen-byte frame with
    /// no body at all, which a thirteen-byte header turns into a spurious one-byte body.
    /// </remarks>
    public const int HeaderLength = 14;

    public byte Selector { get; private set; }

    public ushort Type { get; private set; }

    public uint TotalLength { get; private set; }

    public uint Checksum { get; private set; }

    /// <summary>The eleventh byte, which every observed inbound frame leaves at zero.</summary>
    /// <remarks>
    /// It is named for what it is rather than for what it might be. Across the 190 inbound frames
    /// of the known-good session it never held anything but zero, so nothing here can say what it
    /// carries -- only that the counter is not it.
    /// </remarks>
    public byte Reserved { get; private set; }

    /// <summary>The per-connection frame counter, starting at zero and rising by one.</summary>
    /// <remarks>
    /// The upstream reference reads this counter one byte earlier, which makes it advance in steps
    /// of 256. Reading it here instead gives 189 consecutive increments across the same 190 frames
    /// and no exception, so the boundary is placed by that measurement. Note the samples stop at
    /// 189: the high byte is only ever zero in them, so its role in the counter is inferred from
    /// the two-byte field the older client used, not proven.
    /// </remarks>
    public ushort Sequence { get; private set; }

    public int BodyLength { get; private set; }

    public ReadOnlyMemory<byte> Body { get; private set; }

    public bool OnParsingHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderLength)
        {
            return false;
        }

        Selector = header[0];
        Type = BinaryPrimitives.ReadUInt16LittleEndian(header[1..3]);
        TotalLength = BinaryPrimitives.ReadUInt32LittleEndian(header[3..7]);
        Checksum = BinaryPrimitives.ReadUInt32LittleEndian(header[7..11]);
        Reserved = header[11];
        Sequence = BinaryPrimitives.ReadUInt16LittleEndian(header[12..14]);
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
