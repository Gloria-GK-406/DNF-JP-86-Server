using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Framing;

/// <summary>
/// Decodes the two-byte 0x02A8 body: a single u16. All three client builder sites write one
/// runtime u16 and make no further writer calls, so any other length is not this message.
/// See protocol/game/client-to-server/0x02A8.md.
/// </summary>
internal static class RosterFollowUpPayloadCodec
{
    public const int BodyLength = 2;

    /// <summary>The only value observed so far. It matches the sentinel GET_USERINFO writes for
    /// "no target", but nothing confirms the two share a meaning, so this is documentation
    /// rather than a validation rule.</summary>
    public const ushort ObservedSentinel = 0xFFFF;

    public static RosterFollowUpRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            throw new InvalidDataException(
                $"0x02A8 requires exactly {BodyLength} body bytes; received '{body.Length}'.");
        }

        return new RosterFollowUpRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body));
    }
}
