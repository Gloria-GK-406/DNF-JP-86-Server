using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Framing;

/// <summary>
/// Decodes the fixed 39-byte CLIENT_SPEC_STATISTIC (0x00AB) body:
/// u8 trigger + u8 reported + u8 count(=4) + 4 x (u8 tag, u32 v1, u32 v2).
/// Layout is instruction-confirmed; see protocol/game/client-to-server/0x00AB.md.
/// </summary>
internal static class ClientSpecStatisticPayloadCodec
{
    public const int SlotCount = 4;

    public const int BodyLength = 3 + (SlotCount * 9);

    public static ClientSpecStatisticReportRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            throw new InvalidDataException(
                $"CLIENT_SPEC_STATISTIC requires a {BodyLength}-byte body; received '{body.Length}' bytes.");
        }

        var triggerPath = body[0];
        var reportedFlag = body[1];
        var slotCount = body[2];
        if (slotCount != SlotCount)
        {
            throw new InvalidDataException(
                $"CLIENT_SPEC_STATISTIC declares '{slotCount}' slots; the protocol fixes the count at {SlotCount}.");
        }

        var slots = new ClientSpecStatisticSlot[SlotCount];
        for (var index = 0; index < SlotCount; index++)
        {
            var offset = 3 + (index * 9);
            var tag = body[offset];
            if (tag != index)
            {
                throw new InvalidDataException(
                    $"CLIENT_SPEC_STATISTIC slot '{index}' carries tag '{tag}'; tags are the sequential slot numbers 0..3.");
            }

            slots[index] = new ClientSpecStatisticSlot(
                tag,
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(offset + 1, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(offset + 5, 4)));
        }

        return new ClientSpecStatisticReportRequest(metadata, triggerPath, reportedFlag, slots);
    }
}
