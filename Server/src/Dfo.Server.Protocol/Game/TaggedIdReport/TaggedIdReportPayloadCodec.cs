using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

/// <summary>
/// Decodes the five-byte 0x008F body <c>{u32 id, u8 flag}</c>. All fourteen client builder sites
/// write exactly these two fields with no branches, loops or optional tail, so any other length is
/// not this message. See protocol/game/client-to-server/0x008F.md.
/// </summary>
internal static class TaggedIdReportPayloadCodec
{
    public const int BodyLength = 5;

    public static TaggedIdReportRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            throw new InvalidDataException(
                $"0x008F requires exactly {BodyLength} body bytes; received '{body.Length}'.");
        }

        // No value is rejected: twelve of the fourteen sites hard-code the flag to 1 and two
        // compute it, so the domain of the computed sites is not yet known and must not be
        // guessed at by the parser.
        return new TaggedIdReportRequest(
            metadata,
            BinaryPrimitives.ReadUInt32LittleEndian(body),
            body[4]);
    }
}
