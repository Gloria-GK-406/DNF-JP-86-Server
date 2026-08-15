using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ClientHardwareDetail;

/// <summary>
/// Decodes the fixed 16-byte CLIENT_HARDWARE_DETAIL (0x01DD) body:
/// u8 vendorBucket + u8 cores + u32 clockMhz + u16 memoryMb + u16 gpuVendor + u16 gpuDevice
/// + u16 videoMemoryMb + u8 osKind + u8 osBitness.
/// Layout is instruction-confirmed; see protocol/game/client-to-server/0x01DD.md.
///
/// The body carries no count, tag or checksum field, so length is the only structural invariant
/// there is to enforce -- every 16-byte body is well-formed by definition. Out-of-range values
/// (unknown vendor bucket, unknown OS enum) are preserved as-is and interpreted at the consumer,
/// because rejecting them here would drop telemetry from hardware this record has never sampled.
/// </summary>
internal static class ClientHardwareDetailPayloadCodec
{
    public const int BodyLength = 16;

    public static ClientHardwareDetailReportRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            throw new InvalidDataException(
                $"CLIENT_HARDWARE_DETAIL requires a {BodyLength}-byte body; received '{body.Length}' bytes.");
        }

        return new ClientHardwareDetailReportRequest(
            metadata,
            body[0],
            body[1],
            BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(2, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(6, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(8, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(10, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(12, 2)),
            body[14],
            body[15]);
    }
}
